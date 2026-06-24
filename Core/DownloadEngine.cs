using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using IsparkDownloader2.Models;
using Newtonsoft.Json;

namespace IsparkDownloader2.Core
{
    public class DownloadEngine : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ConfigManager _configManager;
        private readonly BrowserSimulator? _browserSimulator;
        private readonly List<DownloadSegment> _segments = new();
        private bool _isRunning;
        private bool _isPaused;
        private long _totalBytes;
        private long _downloadedBytes;
        private DateTime _startTime;
        private DateTime _lastReportTime;
        private long _lastReportBytes;
        private readonly object _lockObject = new();

        public event EventHandler<DownloadProgress>? ProgressChanged;
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? DownloadCompleted;
        public event EventHandler<Exception>? DownloadFailed;

        public bool IsRunning => _isRunning;
        public bool IsPaused => _isPaused;

        public DownloadEngine(ConfigManager configManager, BrowserSimulator? browserSimulator = null)
        {
            _configManager = configManager;
            _browserSimulator = browserSimulator;
            _httpClient = CreateHttpClient();
        }

        private HttpClient CreateHttpClient()
        {
            var timeout = _configManager?.Config?.ConnectionTimeout ?? 30;

            // 如果启用了浏览器模拟，使用 BrowserSimulator 创建 HttpClient
            if (_browserSimulator != null && _browserSimulator.Profile.EnableSimulation)
            {
                return _browserSimulator.CreateHttpClient(timeout);
            }

            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 20,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                EnableMultipleHttp2Connections = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeout)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd(_configManager?.Config?.UserAgent ?? "IsparkDownloader/2.0");
            return client;
        }

        public async Task DownloadAsync(DownloadTask task, CancellationToken cancellationToken = default)
        {
            try
            {
                _isRunning = true;
                _isPaused = false;
                _startTime = DateTime.Now;
                _lastReportTime = DateTime.Now;
                _lastReportBytes = 0;
                _downloadedBytes = 0;
                StatusChanged?.Invoke(this, "正在获取文件信息...");

                // GitHub 代理加速
                var url = task.Url;
                if (GitHubProxy.IsGitHubUrl(url))
                {
                    url = GitHubProxy.GetProxiedUrl(url);
                    StatusChanged?.Invoke(this, "已启用 GitHub 代理加速");
                }

                // 获取文件信息
                var fileInfo = await GetFileInfoAsync(url, cancellationToken);
                _totalBytes = fileInfo.TotalSize;
                task.TotalSize = fileInfo.TotalSize;
                task.FileName = fileInfo.FileName;
                task.StartTime = DateTime.Now;
                task.SegmentCount = fileInfo.SupportsRange ? Math.Min(task.ThreadCount, _configManager.Config.DefaultThreadCount) : 1;
                task.CompletedSegments = 0;
                task.UserAgent = _configManager?.Config?.UserAgent ?? "IsparkDownloader/2.0";
                task.UseBrowserSimulation = _browserSimulator != null;
                task.ProxyUrl = GitHubProxy.IsGitHubUrl(task.Url) ? GitHubProxy.GetProxiedUrl(task.Url) : "";
                task.FileType = Path.GetExtension(fileInfo.FileName).TrimStart('.');
                if (string.IsNullOrEmpty(task.FileType)) task.FileType = "未知";

                if (!fileInfo.SupportsRange)
                {
                    await DownloadSingleThreadAsync(task, url, cancellationToken);
                    return;
                }

                // 创建保存目录
                Directory.CreateDirectory(task.SavePath);
                var filePath = Path.Combine(task.SavePath, fileInfo.FileName);

                // 分段下载
                var threadCount = task.SegmentCount;
                var segmentSize = _totalBytes / threadCount;

                _segments.Clear();
                for (int i = 0; i < threadCount; i++)
                {
                    var start = i * segmentSize;
                    var end = (i == threadCount - 1) ? _totalBytes - 1 : (i + 1) * segmentSize - 1;
                    _segments.Add(new DownloadSegment
                    {
                        Index = i,
                        StartByte = start,
                        EndByte = end,
                        TempFile = Path.Combine(task.SavePath, $"{fileInfo.FileName}.part{i}")
                    });
                }

                // 并行下载
                var tasks = _segments.Select(s => DownloadSegmentAsync(s, url, cancellationToken)).ToArray();
                await Task.WhenAll(tasks);

                // 合并文件
                StatusChanged?.Invoke(this, "正在合并文件...");
                await MergeFilesAsync(filePath, cancellationToken);

                task.CompletedSegments = task.SegmentCount;
                task.Status = DownloadStatus.Completed;
                task.ElapsedTime = DateTime.Now - (task.StartTime ?? DateTime.Now);
                StatusChanged?.Invoke(this, "下载完成");
                DownloadCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                StatusChanged?.Invoke(this, $"下载失败: {ex.Message}");
                DownloadFailed?.Invoke(this, ex);
            }
            finally
            {
                _isRunning = false;
            }
        }

        private async Task<FileInfoResult> GetFileInfoAsync(string url, CancellationToken cancellationToken)
        {
            // 策略1：尝试 HEAD 请求
            try
            {
                using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                ApplyBrowserHeaders(headRequest);
                var headResponse = await _httpClient.SendAsync(headRequest, cancellationToken);

                if (headResponse.IsSuccessStatusCode)
                {
                    var fileName = GetFileNameFromResponse(headResponse);
                    var totalSize = headResponse.Content.Headers.ContentLength ?? 0;
                    var supportsRange = headResponse.Headers.AcceptRanges?.Contains("bytes") ?? false;

                    return new FileInfoResult
                    {
                        FileName = fileName,
                        TotalSize = totalSize,
                        SupportsRange = supportsRange && totalSize > 0
                    };
                }
            }
            catch { }

            // 策略2：HEAD 失败，使用 GET 请求只读取响应头（带 Range）
            try
            {
                using var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyBrowserHeaders(getRequest);
                getRequest.Headers.Range = new RangeHeaderValue(0, 0);

                var response = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.PartialContent || response.IsSuccessStatusCode)
                {
                    var fn = GetFileNameFromResponse(response);
                    var size = response.Content.Headers.ContentLength ?? 0;

                    // 如果 Content-Length 是 1（因为我们只请求了 1 字节），尝试从 Content-Range 获取总大小
                    if (size <= 1 && response.Content.Headers.ContentRange != null)
                    {
                        size = response.Content.Headers.ContentRange.Length ?? 0;
                    }

                    var rangeSupport = response.StatusCode == System.Net.HttpStatusCode.PartialContent ||
                                      (response.Headers.AcceptRanges?.Contains("bytes") ?? false);

                    return new FileInfoResult
                    {
                        FileName = fn,
                        TotalSize = size,
                        SupportsRange = rangeSupport && size > 0
                    };
                }
            }
            catch { }

            // 策略3：Range 也不支持，使用普通 GET 请求
            using var plainRequest = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBrowserHeaders(plainRequest);

            var plainResponse = await _httpClient.SendAsync(plainRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            plainResponse.EnsureSuccessStatusCode();

            var fileName2 = GetFileNameFromResponse(plainResponse);
            var totalSize2 = plainResponse.Content.Headers.ContentLength ?? 0;

            return new FileInfoResult
            {
                FileName = fileName2,
                TotalSize = totalSize2,
                SupportsRange = false
            };
        }

        private async Task DownloadSingleThreadAsync(DownloadTask task, string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBrowserHeaders(request);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var fileName = GetFileNameFromResponse(response);
            task.FileName = fileName;
            var totalSize = response.Content.Headers.ContentLength ?? 0;
            if (totalSize > 0)
            {
                _totalBytes = totalSize;
                task.TotalSize = totalSize;
            }
            var filePath = Path.Combine(task.SavePath, fileName);

            Directory.CreateDirectory(task.SavePath);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                while (_isPaused)
                {
                    await Task.Delay(100, cancellationToken);
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                lock (_lockObject)
                {
                    _downloadedBytes += read;
                }

                ReportProgress();
            }

            task.CompletedSegments = 1;
            task.ElapsedTime = DateTime.Now - (task.StartTime ?? DateTime.Now);
            task.Status = DownloadStatus.Completed;
            StatusChanged?.Invoke(this, "下载完成");
            DownloadCompleted?.Invoke(this, EventArgs.Empty);
        }

        private async Task DownloadSegmentAsync(DownloadSegment segment, string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(segment.StartByte, segment.EndByte);
            ApplyBrowserHeaders(request);

            var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(segment.TempFile, FileMode.Create, FileAccess.Write);

            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                while (_isPaused)
                {
                    await Task.Delay(100, cancellationToken);
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                lock (_lockObject)
                {
                    segment.DownloadedBytes += read;
                    _downloadedBytes = _segments.Sum(s => s.DownloadedBytes);
                }

                ReportProgress();
            }
        }

        private void ApplyBrowserHeaders(HttpRequestMessage request)
        {
            if (_browserSimulator != null && _browserSimulator.Profile.EnableSimulation)
            {
                _browserSimulator.ApplyToRequest(request);
            }
        }

        private async Task MergeFilesAsync(string filePath, CancellationToken cancellationToken)
        {
            await using var outputStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);

            foreach (var segment in _segments.OrderBy(s => s.Index))
            {
                if (!File.Exists(segment.TempFile))
                    continue;

                await using var inputStream = new FileStream(segment.TempFile, FileMode.Open, FileAccess.Read);
                await inputStream.CopyToAsync(outputStream, cancellationToken);
            }

            // 删除临时文件
            foreach (var segment in _segments)
            {
                try
                {
                    if (File.Exists(segment.TempFile))
                        File.Delete(segment.TempFile);
                }
                catch { }
            }
        }

        private string GetFileNameFromResponse(HttpResponseMessage response)
        {
            // 从 Content-Disposition 获取文件名
            var contentDisposition = response.Content.Headers.ContentDisposition;
            if (contentDisposition?.FileName != null)
            {
                return contentDisposition.FileName.Trim('"');
            }

            // 从 URL 获取文件名
            var uri = response.RequestMessage?.RequestUri;
            if (uri != null)
            {
                var fileName = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(fileName))
                    return fileName;
            }

            return $"download_{DateTime.Now:yyyyMMddHHmmss}";
        }

        private void ReportProgress()
        {
            var now = DateTime.Now;
            var timeDiff = (now - _lastReportTime).TotalSeconds;
            double speed;
            if (timeDiff > 0.5)
            {
                // 计算瞬时速度
                var bytesDiff = _downloadedBytes - _lastReportBytes;
                speed = bytesDiff / timeDiff;
                _lastReportTime = now;
                _lastReportBytes = _downloadedBytes;
            }
            else
            {
                // 时间太短，使用平均速度
                var elapsed = now - _startTime;
                speed = elapsed.TotalSeconds > 0 ? _downloadedBytes / elapsed.TotalSeconds : 0;
            }

            var progress = _totalBytes > 0 ? (double)_downloadedBytes / _totalBytes * 100 : 0;

            ProgressChanged?.Invoke(this, new DownloadProgress
            {
                TotalBytes = _totalBytes,
                DownloadedBytes = _downloadedBytes,
                Speed = speed,
                Progress = progress
            });
        }

        public void Pause()
        {
            _isPaused = true;
            StatusChanged?.Invoke(this, "已暂停");
        }

        public void Resume()
        {
            _isPaused = false;
            StatusChanged?.Invoke(this, "继续下载");
        }

        public void Cancel()
        {
            _isRunning = false;
            StatusChanged?.Invoke(this, "已取消");
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class DownloadProgress
    {
        public long TotalBytes { get; set; }
        public long DownloadedBytes { get; set; }
        public double Speed { get; set; }
        public double Progress { get; set; }
    }

    public class FileInfoResult
    {
        public string FileName { get; set; } = "";
        public long TotalSize { get; set; }
        public bool SupportsRange { get; set; }
    }

    public class DownloadSegment
    {
        public int Index { get; set; }
        public long StartByte { get; set; }
        public long EndByte { get; set; }
        public long DownloadedBytes { get; set; }
        public string TempFile { get; set; } = "";
    }
}
