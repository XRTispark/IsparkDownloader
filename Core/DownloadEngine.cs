using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Core
{
    public class DownloadProgressEventArgs : EventArgs
    {
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double Speed { get; set; }
        public int ProgressPercentage { get; set; }
    }

    public class DownloadEngine : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, CancellationTokenSource> _cancellationTokens = new();
        private readonly Dictionary<string, List<Stream>> _fileStreams = new();
        private bool _disposed;

        public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;
        public event EventHandler? DownloadCompleted;
        public event EventHandler<string>? DownloadFailed;

        public DownloadEngine()
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 20,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                EnableMultipleHttp2Connections = true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        }

        public async Task<bool> DownloadAsync(DownloadTask task)
        {
            var cts = new CancellationTokenSource();
            _cancellationTokens[task.Id] = cts;

            try
            {
                task.Status = DownloadStatus.Downloading;

                // 获取文件信息
                var (supportsRange, totalSize) = await GetFileInfoAsync(task.Url);
                task.TotalSize = totalSize;

                // 确定文件名
                if (string.IsNullOrEmpty(task.FileName))
                {
                    task.FileName = GetFileNameFromUrl(task.Url);
                }

                // 确保目录存在
                Directory.CreateDirectory(task.SavePath);

                var fullPath = task.FullPath;
                var tempPath = fullPath + ".tmp";

                // 检查是否支持断点续传
                if (supportsRange && task.ThreadCount > 1 && totalSize > 0)
                {
                    await MultiThreadDownloadAsync(task, tempPath, totalSize, cts.Token);
                }
                else
                {
                    await SingleThreadDownloadAsync(task, tempPath, cts.Token);
                }

                // 下载完成，重命名文件
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
                File.Move(tempPath, fullPath);

                task.Status = DownloadStatus.Completed;
                task.CompleteTime = DateTime.Now;
                task.Speed = 0;
                DownloadCompleted?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (OperationCanceledException)
            {
                task.Status = DownloadStatus.Cancelled;
                return false;
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                task.ErrorMessage = ex.Message;
                DownloadFailed?.Invoke(this, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokens.Remove(task.Id);
            }
        }

        private async Task<(bool supportsRange, long totalSize)> GetFileInfoAsync(string url)
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            bool supportsRange = response.Headers.AcceptRanges?.Contains("bytes") ?? false;
            long totalSize = response.Content.Headers.ContentLength ?? 0;

            return (supportsRange, totalSize);
        }

        private async Task SingleThreadDownloadAsync(DownloadTask task, string tempPath, CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(task.Url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            if (totalBytes > 0) task.TotalSize = totalBytes;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastReportTime = DateTime.Now;
            long lastReportBytes = 0;

            while (true)
            {
                var read = await contentStream.ReadAsync(buffer, ct);
                if (read == 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                downloadedBytes += read;
                task.DownloadedSize = downloadedBytes;

                // 速度限制
                if (task.SpeedLimit > 0)
                {
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    var expectedTime = downloadedBytes / (double)task.SpeedLimit;
                    if (expectedTime > elapsed)
                    {
                        await Task.Delay((int)((expectedTime - elapsed) * 1000), ct);
                    }
                }

                // 报告进度
                var now = DateTime.Now;
                if ((now - lastReportTime).TotalMilliseconds >= 500)
                {
                    var timeDiff = (now - lastReportTime).TotalSeconds;
                    var bytesDiff = downloadedBytes - lastReportBytes;
                    var speed = bytesDiff / timeDiff;

                    task.Speed = speed;
                    ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                    {
                        DownloadedBytes = downloadedBytes,
                        TotalBytes = totalBytes,
                        Speed = speed,
                        ProgressPercentage = totalBytes > 0 ? (int)((double)downloadedBytes / totalBytes * 100) : 0
                    });

                    lastReportTime = now;
                    lastReportBytes = downloadedBytes;
                }
            }

            task.DownloadedSize = downloadedBytes;
            task.Speed = 0;
        }

        private async Task MultiThreadDownloadAsync(DownloadTask task, string tempPath, long totalSize, CancellationToken ct)
        {
            var chunkSize = totalSize / task.ThreadCount;
            var tasks = new List<Task>();
            var progressLock = new object();
            long totalDownloaded = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 创建临时文件并设置大小
            using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                fs.SetLength(totalSize);
            }

            for (int i = 0; i < task.ThreadCount; i++)
            {
                var start = i * chunkSize;
                var end = (i == task.ThreadCount - 1) ? totalSize - 1 : (start + chunkSize - 1);

                var threadTask = Task.Run(async () =>
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, task.Url);
                    request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                    await using var fileStream = new FileStream(tempPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                    fileStream.Seek(start, SeekOrigin.Begin);

                    var buffer = new byte[8192];
                    while (true)
                    {
                        var read = await contentStream.ReadAsync(buffer, ct);
                        if (read == 0) break;

                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);

                        lock (progressLock)
                        {
                            totalDownloaded += read;
                            task.DownloadedSize = totalDownloaded;
                        }

                        // 速度限制（粗略控制）
                        if (task.SpeedLimit > 0)
                        {
                            var elapsed = stopwatch.Elapsed.TotalSeconds;
                            var expectedTime = totalDownloaded / (double)task.SpeedLimit;
                            if (expectedTime > elapsed)
                            {
                                await Task.Delay((int)((expectedTime - elapsed) * 1000 / task.ThreadCount), ct);
                            }
                        }
                    }
                }, ct);

                tasks.Add(threadTask);
            }

            // 进度报告任务
            var reportTask = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested && task.Status == DownloadStatus.Downloading)
                {
                    await Task.Delay(500, ct);
                    lock (progressLock)
                    {
                        task.Speed = totalDownloaded / Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
                        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
                        {
                            DownloadedBytes = totalDownloaded,
                            TotalBytes = totalSize,
                            Speed = task.Speed,
                            ProgressPercentage = (int)((double)totalDownloaded / totalSize * 100)
                        });
                    }
                }
            }, ct);

            await Task.WhenAll(tasks);
            stopwatch.Stop();
            task.Speed = 0;
        }

        public void PauseDownload(string taskId)
        {
            if (_cancellationTokens.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
            }
        }

        public void CancelDownload(string taskId)
        {
            if (_cancellationTokens.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                _cancellationTokens.Remove(taskId);
            }
        }

        private string GetFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var path = uri.AbsolutePath;
                var fileName = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(fileName))
                {
                    return Uri.UnescapeDataString(fileName);
                }
            }
            catch { }
            return $"download_{DateTime.Now:yyyyMMddHHmmss}";
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var cts in _cancellationTokens.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                _cancellationTokens.Clear();
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}
