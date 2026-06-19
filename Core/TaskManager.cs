using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using IsparkDownloader2.Core.CloudDrive;
using IsparkDownloader2.Models;
using Newtonsoft.Json;

namespace IsparkDownloader2.Core
{
    public class TaskManager
    {
        private readonly ObservableCollection<DownloadTask> _tasks = new();
        private readonly DownloadEngine _httpEngine;
        private readonly TorrentEngine _torrentEngine;
        private readonly CloudDriveEngine _cloudDriveEngine;
        private readonly TrackerConfigManager _trackerConfig;
        private readonly string _configPath;
        private readonly System.Timers.Timer _saveTimer;
        private readonly int _maxConcurrentDownloads;
        private readonly object _lock = new();

        public ObservableCollection<DownloadTask> Tasks => _tasks;

        public TrackerConfigManager TrackerConfig => _trackerConfig;
        public TorrentEngine TorrentEngine => _torrentEngine;
        public DownloadEngine HttpEngine => _httpEngine;
        public CloudDriveEngine CloudDriveEngine => _cloudDriveEngine;

        public event EventHandler? TaskAdded;
        public event EventHandler? TaskRemoved;
        public event EventHandler? TaskStatusChanged;

        public TaskManager(string configPath, int maxConcurrentDownloads = 3)
        {
            _httpEngine = new DownloadEngine();
            _torrentEngine = new TorrentEngine();
            var cloudConfigPath = Path.Combine(Path.GetDirectoryName(configPath)!, "cloud_accounts.json");
            var cloudConfigManager = new CloudDriveConfigManager(cloudConfigPath);
            _cloudDriveEngine = new CloudDriveEngine(cloudConfigManager);
            _trackerConfig = new TrackerConfigManager();
            _configPath = configPath;
            _maxConcurrentDownloads = maxConcurrentDownloads;

            // 订阅 BT 引擎事件
            _torrentEngine.ProgressChanged += OnTorrentProgressChanged;
            _torrentEngine.DownloadCompleted += OnTorrentDownloadCompleted;

            // 自动保存任务列表
            _saveTimer = new System.Timers.Timer(10000);
            _saveTimer.Elapsed += (s, e) => SaveTasks();
            _saveTimer.Start();

            LoadTasks();
        }

        /// <summary>添加 HTTP/HTTPS/FTP 下载任务</summary>
        public DownloadTask AddTask(string url, string? savePath = null, string? fileName = null,
            int threadCount = 8, long speedLimit = 0)
        {
            var task = new DownloadTask
            {
                Url = url,
                SavePath = savePath ?? GetDefaultSavePath(),
                FileName = fileName ?? string.Empty,
                ThreadCount = threadCount,
                SpeedLimit = speedLimit,
                Type = GetDownloadType(url),
                Status = DownloadStatus.Pending
            };

            lock (_lock) { _tasks.Add(task); }

            TaskAdded?.Invoke(this, EventArgs.Empty);
            SaveTasks();
            _ = ProcessQueueAsync();
            return task;
        }

        /// <summary>添加磁力链接下载任务</summary>
        public DownloadTask AddMagnetTask(string magnetUrl, string? savePath = null,
            long speedLimit = 0, List<string>? trackers = null)
        {
            var task = new DownloadTask
            {
                Url = magnetUrl,
                SavePath = savePath ?? GetDefaultSavePath(),
                Type = DownloadType.Magnet,
                Status = DownloadStatus.Pending,
                SpeedLimit = speedLimit,
                FileName = "正在解析磁力链接..."
            };

            lock (_lock) { _tasks.Add(task); }

            TaskAdded?.Invoke(this, EventArgs.Empty);
            SaveTasks();
            _ = ProcessQueueAsync();
            return task;
        }

        /// <summary>添加种子文件下载任务</summary>
        public DownloadTask AddTorrentTask(string torrentFilePath, string? savePath = null,
            long speedLimit = 0, List<string>? trackers = null)
        {
            var task = new DownloadTask
            {
                Url = torrentFilePath,
                SavePath = savePath ?? GetDefaultSavePath(),
                Type = DownloadType.Torrent,
                Status = DownloadStatus.Pending,
                SpeedLimit = speedLimit,
                TorrentFilePath = torrentFilePath,
                FileName = Path.GetFileName(torrentFilePath)
            };

            lock (_lock) { _tasks.Add(task); }

            TaskAdded?.Invoke(this, EventArgs.Empty);
            SaveTasks();
            _ = ProcessQueueAsync();
            return task;
        }

        public void RemoveTask(string taskId)
        {
            lock (_lock)
            {
                var task = _tasks.FirstOrDefault(t => t.Id == taskId);
                if (task != null)
                {
                    if (task.Status == DownloadStatus.Downloading)
                    {
                        if (task.Type == DownloadType.Magnet || task.Type == DownloadType.Torrent)
                            _ = _torrentEngine.CancelDownloadAsync(taskId);
                        else if (task.Type == DownloadType.CloudDrive)
                            _cloudDriveEngine.CancelDownload(taskId);
                        else
                            _httpEngine.CancelDownload(taskId);
                    }
                    _tasks.Remove(task);
                    TaskRemoved?.Invoke(this, EventArgs.Empty);
                }
            }
            SaveTasks();
        }

        public async Task StartTaskAsync(string taskId)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null || task.Status == DownloadStatus.Downloading) return;

            task.Status = DownloadStatus.Pending;
            await ProcessQueueAsync();
        }

        public void PauseTask(string taskId)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task?.Status != DownloadStatus.Downloading) return;

            if (task.Type == DownloadType.Magnet || task.Type == DownloadType.Torrent)
                _ = _torrentEngine.PauseDownloadAsync(taskId);
            else if (task.Type == DownloadType.CloudDrive)
                _cloudDriveEngine.PauseDownload(taskId);
            else
                _httpEngine.PauseDownload(taskId);

            task.Status = DownloadStatus.Paused;
            TaskStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        public void CancelTask(string taskId)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;

            if (task.Status == DownloadStatus.Downloading)
            {
                if (task.Type == DownloadType.Magnet || task.Type == DownloadType.Torrent)
                    _ = _torrentEngine.CancelDownloadAsync(taskId);
                else if (task.Type == DownloadType.CloudDrive)
                    _cloudDriveEngine.CancelDownload(taskId);
                else
                    _httpEngine.CancelDownload(taskId);
            }
            task.Status = DownloadStatus.Cancelled;
            TaskStatusChanged?.Invoke(this, EventArgs.Empty);
            _ = ProcessQueueAsync();
        }

        public async Task StartAllAsync()
        {
            foreach (var task in _tasks.Where(t =>
                t.Status == DownloadStatus.Paused || t.Status == DownloadStatus.Failed))
            {
                task.Status = DownloadStatus.Pending;
            }
            await ProcessQueueAsync();
        }

        public void PauseAll()
        {
            foreach (var task in _tasks.Where(t => t.Status == DownloadStatus.Downloading).ToList())
            {
                PauseTask(task.Id);
            }
        }

        public void RemoveCompleted()
        {
            lock (_lock)
            {
                var completed = _tasks.Where(t => t.Status == DownloadStatus.Completed).ToList();
                foreach (var task in completed) _tasks.Remove(task);
                if (completed.Count > 0) TaskRemoved?.Invoke(this, EventArgs.Empty);
            }
            SaveTasks();
        }

        // ===== BT 专用方法 =====

        /// <summary>启动 BT 引擎</summary>
        public async Task StartTorrentEngineAsync(int port = 6881)
        {
            if (!_torrentEngine.IsRunning)
            {
                await _torrentEngine.StartAsync(port);
            }
        }

        /// <summary>获取 Tracker 详细状态</summary>
        public async Task<List<TrackerStatusItem>> GetTrackerStatusAsync(string taskId)
        {
            return await _torrentEngine.GetTrackerStatusAsync(taskId);
        }

        /// <summary>为任务添加 Tracker</summary>
        public async Task AddTrackerToTaskAsync(string taskId, string trackerUrl)
        {
            await _torrentEngine.AddTrackersAsync(taskId, new List<string> { trackerUrl });
        }

        // ===== 网盘任务方法 =====

        /// <summary>添加网盘分享链接下载任务</summary>
        public DownloadTask AddCloudDriveShareTask(string shareUrl, string fileName, long fileSize,
            CloudDriveType driveType, string shareId, string shareToken, string fileId,
            string? savePath = null, long speedLimit = 0)
        {
            var task = new DownloadTask
            {
                Url = shareUrl,
                SavePath = savePath ?? GetDefaultSavePath(),
                FileName = fileName,
                TotalSize = fileSize,
                Type = DownloadType.CloudDrive,
                Status = DownloadStatus.Pending,
                SpeedLimit = speedLimit
            };

            // 将网盘信息存储在任务的扩展字段中（通过 TorrentName 字段复用）
            task.TorrentName = $"{driveType}|{shareId}|{shareToken}|{fileId}";

            lock (_lock) { _tasks.Add(task); }

            TaskAdded?.Invoke(this, EventArgs.Empty);
            SaveTasks();
            _ = ProcessQueueAsync();
            return task;
        }

        // ===== 队列处理 =====

        private async Task ProcessQueueAsync()
        {
            while (true)
            {
                var downloadingCount = _tasks.Count(t => t.Status == DownloadStatus.Downloading);
                if (downloadingCount >= _maxConcurrentDownloads) break;

                var nextTask = _tasks.FirstOrDefault(t => t.Status == DownloadStatus.Pending);
                if (nextTask == null) break;

                if (nextTask.Type == DownloadType.Magnet || nextTask.Type == DownloadType.Torrent)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            nextTask.Status = DownloadStatus.Downloading;
                            TaskStatusChanged?.Invoke(this, EventArgs.Empty);

                            if (!_torrentEngine.IsRunning)
                                await _torrentEngine.StartAsync();

                            if (nextTask.Type == DownloadType.Magnet)
                            {
                                var trackers = _trackerConfig.GetAllTrackers();
                                await _torrentEngine.AddMagnetLinkAsync(nextTask, trackers);
                                await _torrentEngine.StartDownloadAsync(nextTask.Id);
                            }
                            else if (nextTask.Type == DownloadType.Torrent && !string.IsNullOrEmpty(nextTask.TorrentFilePath))
                            {
                                var trackers = _trackerConfig.GetAllTrackers();
                                await _torrentEngine.AddTorrentFileAsync(nextTask, nextTask.TorrentFilePath, trackers);
                                await _torrentEngine.StartDownloadAsync(nextTask.Id);
                            }
                        }
                        catch (Exception ex)
                        {
                            nextTask.Status = DownloadStatus.Failed;
                            nextTask.ErrorMessage = ex.Message;
                            TaskStatusChanged?.Invoke(this, EventArgs.Empty);
                        }
                    });
                }
                else if (nextTask.Type == DownloadType.CloudDrive)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            nextTask.Status = DownloadStatus.Downloading;
                            TaskStatusChanged?.Invoke(this, EventArgs.Empty);

                            // 解析存储的网盘信息
                            var parts = nextTask.TorrentName.Split('|');
                            if (parts.Length >= 4)
                            {
                                var driveType = Enum.Parse<CloudDriveType>(parts[0]);
                                var shareId = parts[1];
                                var shareToken = parts[2];
                                var fileId = parts[3];

                                await _cloudDriveEngine.DownloadShareFileAsync(nextTask, driveType, shareId, shareToken, fileId);
                            }
                            else
                            {
                                nextTask.Status = DownloadStatus.Failed;
                                nextTask.ErrorMessage = "网盘任务信息格式错误";
                            }

                            TaskStatusChanged?.Invoke(this, EventArgs.Empty);
                            await ProcessQueueAsync();
                        }
                        catch (Exception ex)
                        {
                            nextTask.Status = DownloadStatus.Failed;
                            nextTask.ErrorMessage = ex.Message;
                            TaskStatusChanged?.Invoke(this, EventArgs.Empty);
                        }
                    });
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        await _httpEngine.DownloadAsync(nextTask);
                        TaskStatusChanged?.Invoke(this, EventArgs.Empty);
                        await ProcessQueueAsync();
                    });
                }

                await Task.Delay(100);
            }
        }

        // ===== 事件处理 =====

        private void OnTorrentProgressChanged(object? sender, TorrentProgressEventArgs e)
        {
            var task = _tasks.FirstOrDefault(t => t.Id == e.TaskId);
            if (task == null) return;

            task.Seeds = e.Seeds;
            task.Peers = e.Peers;
            task.UploadSpeed = e.UploadSpeed;
            TaskStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        private void OnTorrentDownloadCompleted(object? sender, string taskId)
        {
            TaskStatusChanged?.Invoke(this, EventArgs.Empty);
            _ = ProcessQueueAsync();
        }

        // ===== 辅助方法 =====

        private DownloadType GetDownloadType(string url)
        {
            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return DownloadType.Magnet;
            if (url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase))
                return DownloadType.Torrent;
            if (url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                return DownloadType.Ftp;
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return DownloadType.Https;
            return DownloadType.Http;
        }

        private string GetDefaultSavePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private void SaveTasks()
        {
            try
            {
                var data = JsonConvert.SerializeObject(_tasks.Select(t => new
                {
                    t.Id, t.Url, t.FileName, t.SavePath,
                    t.TotalSize, t.DownloadedSize, t.Status,
                    t.ThreadCount, t.SpeedLimit, t.Type, t.CreateTime,
                    t.TorrentFilePath, t.TorrentName, t.InfoHash
                }), Formatting.Indented);

                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.WriteAllText(_configPath, data);
            }
            catch { }
        }

        private void LoadTasks()
        {
            try
            {
                if (!File.Exists(_configPath)) return;
                var json = File.ReadAllText(_configPath);
                var items = JsonConvert.DeserializeObject<List<dynamic>>(json);
                if (items == null) return;

                foreach (var item in items)
                {
                    var task = new DownloadTask
                    {
                        Id = item.Id,
                        Url = item.Url,
                        FileName = item.FileName,
                        SavePath = item.SavePath,
                        TotalSize = item.TotalSize,
                        DownloadedSize = item.DownloadedSize,
                        ThreadCount = item.ThreadCount ?? 8,
                        SpeedLimit = item.SpeedLimit ?? 0,
                        CreateTime = item.CreateTime,
                        TorrentFilePath = item.TorrentFilePath ?? "",
                        TorrentName = item.TorrentName ?? "",
                        InfoHash = item.InfoHash ?? ""
                    };

                    var statusStr = item.Status.ToString();
                    if (statusStr == "Downloading" || statusStr == "Pending")
                        task.Status = DownloadStatus.Paused;
                    else
                        task.Status = Enum.Parse<DownloadStatus>(statusStr);

                    // 恢复类型
                    var typeStr = item.Type?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(typeStr))
                        task.Type = Enum.Parse<DownloadType>(typeStr);

                    _tasks.Add(task);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _saveTimer?.Stop();
            _saveTimer?.Dispose();
            SaveTasks();
            _httpEngine?.Dispose();
            _torrentEngine?.Dispose();
            _cloudDriveEngine?.Dispose();
        }
    }
}