using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using IsparkDownloader2.Core;
using IsparkDownloader2.Core.CloudDrive;
using IsparkDownloader2.Models;
using Microsoft.Win32;

namespace IsparkDownloader2.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => _execute(parameter);

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly TaskManager _taskManager;
        private readonly ConfigManager _configManager;
        private DownloadTask? _selectedTask;
        private string _newUrl = string.Empty;
        private string _statusText = "就绪";
        private int _totalTasks;
        private int _activeTasks;
        private int _completedTasks;

        public ObservableCollection<DownloadTask> Tasks => _taskManager.Tasks;

        public DownloadTask? SelectedTask
        {
            get => _selectedTask;
            set { _selectedTask = value; OnPropertyChanged(); }
        }

        public string NewUrl
        {
            get => _newUrl;
            set { _newUrl = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public int TotalTasks
        {
            get => _totalTasks;
            set { _totalTasks = value; OnPropertyChanged(); }
        }

        public int ActiveTasks
        {
            get => _activeTasks;
            set { _activeTasks = value; OnPropertyChanged(); }
        }

        public int CompletedTasks
        {
            get => _completedTasks;
            set { _completedTasks = value; OnPropertyChanged(); }
        }

        // 命令
        public ICommand AddTaskCommand { get; }
        public ICommand RemoveTaskCommand { get; }
        public ICommand StartTaskCommand { get; }
        public ICommand PauseTaskCommand { get; }
        public ICommand StartAllCommand { get; }
        public ICommand PauseAllCommand { get; }
        public ICommand RemoveCompletedCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand BrowseSavePathCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        // BT 相关命令
        public ICommand SelectTorrentFileCommand { get; }
        public ICommand ShowTrackerConfigCommand { get; }
        public ICommand ShowTorrentDetailCommand { get; }
        // 网盘相关命令
        public ICommand ShowCloudDriveConfigCommand { get; }
        public ICommand ParseCloudShareCommand { get; }
        // 更新命令
        public ICommand CheckUpdateCommand { get; }

        public AppConfig Config => _configManager.Config;
        public CloudDriveEngine CloudDriveEngine => _taskManager.CloudDriveEngine;

        public MainViewModel()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IsparkDownloader2"
            );
            Directory.CreateDirectory(appDataPath);

            _configManager = new ConfigManager();
            _taskManager = new TaskManager(
                Path.Combine(appDataPath, "tasks.json"),
                _configManager.Config.MaxConcurrentDownloads
            );

            _taskManager.TaskAdded += (s, e) => UpdateStats();
            _taskManager.TaskRemoved += (s, e) => UpdateStats();
            _taskManager.TaskStatusChanged += (s, e) => UpdateStats();

            AddTaskCommand = new RelayCommand(_ => AddTask(), _ => !string.IsNullOrWhiteSpace(NewUrl));
            RemoveTaskCommand = new RelayCommand(_ => RemoveTask(), _ => SelectedTask != null);
            StartTaskCommand = new RelayCommand(_ => StartTask(),_ => SelectedTask?.Status == DownloadStatus.Paused || SelectedTask?.Status == DownloadStatus.Failed || SelectedTask?.Status == DownloadStatus.Pending);
            PauseTaskCommand = new RelayCommand(_ => PauseTask(), _ => SelectedTask?.Status == DownloadStatus.Downloading);
            StartAllCommand = new RelayCommand(_ => StartAll(), _ => Tasks.Any(t => t.Status == DownloadStatus.Paused || t.Status == DownloadStatus.Failed || t.Status == DownloadStatus.Pending));
            PauseAllCommand = new RelayCommand(_ => PauseAll(), _ => Tasks.Any(t => t.Status == DownloadStatus.Downloading));
            RemoveCompletedCommand = new RelayCommand(_ => RemoveCompleted(), _ => Tasks.Any(t => t.Status == DownloadStatus.Completed));
            OpenFolderCommand = new RelayCommand(_ => OpenFolder(), _ => SelectedTask != null);
            BrowseSavePathCommand = new RelayCommand(_ => BrowseSavePath());
            ShowSettingsCommand = new RelayCommand(_ => ShowSettings());
            SelectTorrentFileCommand = new RelayCommand(_ => SelectTorrentFile());
            ShowTrackerConfigCommand = new RelayCommand(_ => ShowTrackerConfig());
            ShowTorrentDetailCommand = new RelayCommand(_ => ShowTorrentDetail(), _ => SelectedTask != null && (SelectedTask.Type == DownloadType.Magnet || SelectedTask.Type == DownloadType.Torrent));
            ShowCloudDriveConfigCommand = new RelayCommand(_ => ShowCloudDriveConfig());
            ParseCloudShareCommand = new RelayCommand(_ => ParseCloudShareLink(), _ => !string.IsNullOrWhiteSpace(NewUrl));
            CheckUpdateCommand = new RelayCommand(_ => CheckUpdate(), _ => true);

            UpdateStats();
        }

        private void AddTask()
        {
            if (string.IsNullOrWhiteSpace(NewUrl)) return;

            try
            {
                var url = NewUrl.Trim();

                // 自动识别下载类型
                if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                {
                    // 磁力链接
                    _taskManager.AddMagnetTask(url, _configManager.Config.DefaultSavePath, _configManager.Config.DefaultSpeedLimit);
                    StatusText = "磁力链接任务已添加";
                }
                else if (url.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    // 在线 .torrent 文件
                    _taskManager.AddTask(url, _configManager.Config.DefaultSavePath, null,
                        _configManager.Config.DefaultThreadCount, _configManager.Config.DefaultSpeedLimit);
                    StatusText = "种子文件下载任务已添加";
                }
                else
                {
                    var uri = new Uri(url);
                    _taskManager.AddTask(url, _configManager.Config.DefaultSavePath, null,
                        _configManager.Config.DefaultThreadCount, _configManager.Config.DefaultSpeedLimit);
                    StatusText = "任务已添加";
                }

                NewUrl = string.Empty;
            }
            catch (UriFormatException)
            {
                MessageBox.Show("请输入有效的 URL", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveTask()
        {
            if (SelectedTask == null) return;
            var result = MessageBox.Show($"确定要删除任务 \"{SelectedTask.FileName}\" 吗？", "确认删除",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                _taskManager.RemoveTask(SelectedTask.Id);
                SelectedTask = null;
            }
        }

        private async void StartTask()
        {
            if (SelectedTask == null) return;
            await _taskManager.StartTaskAsync(SelectedTask.Id);
        }

        private void PauseTask()
        {
            if (SelectedTask == null) return;
            _taskManager.PauseTask(SelectedTask.Id);
        }

        private async void StartAll()
        {
            await _taskManager.StartAllAsync();
        }

        private void PauseAll()
        {
            _taskManager.PauseAll();
        }

        private void RemoveCompleted()
        {
            _taskManager.RemoveCompleted();
        }

        private void OpenFolder()
        {
            if (SelectedTask == null) return;
            var path = SelectedTask.FullPath;
            if (File.Exists(path))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            else
            {
                System.Diagnostics.Process.Start("explorer.exe", SelectedTask.SavePath);
            }
        }

        private void BrowseSavePath()
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择保存目录",
                FolderName = _configManager.Config.DefaultSavePath
            };
            if (dialog.ShowDialog() == true)
            {
                _configManager.Config.DefaultSavePath = dialog.FolderName;
                _configManager.Save();
            }
        }

        private void ShowSettings()
        {
            var settingsWindow = new Views.SettingsWindow(_configManager);
            settingsWindow.ShowDialog();
        }

        // ===== 网盘专用方法 =====

        /// <summary>显示网盘配置窗口</summary>
        private void ShowCloudDriveConfig()
        {
            var window = new Views.CloudDriveConfigWindow(_taskManager.CloudDriveEngine);
            window.ShowDialog();
        }

        /// <summary>解析网盘分享链接</summary>
        private async void ParseCloudShareLink()
        {
            if (string.IsNullOrWhiteSpace(NewUrl)) return;

            var url = NewUrl.Trim();

            try
            {
                StatusText = "正在解析分享链接...";
                var (driveType, shareInfo) = await _taskManager.CloudDriveEngine.ParseShareLinkAsync(url);

                if (driveType == null || !shareInfo.Success)
                {
                    MessageBox.Show($"解析失败: {shareInfo.ErrorMessage}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText = "分享链接解析失败";
                    return;
                }

                // 获取分享文件列表
                var files = await _taskManager.CloudDriveEngine.GetShareFileListAsync(driveType.Value, shareInfo.ShareId, shareInfo.ShareToken);

                if (files.Count == 0)
                {
                    MessageBox.Show("分享链接中没有可下载的文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusText = "分享链接中没有文件";
                    return;
                }

                // 显示文件选择窗口
                var selectWindow = new Views.CloudDriveFileSelectWindow(files, shareInfo, driveType.Value, _taskManager, _configManager.Config.DefaultSavePath);
                selectWindow.ShowDialog();

                NewUrl = string.Empty;
                StatusText = $"已添加网盘分享任务 ({files.Count} 个文件)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解析分享链接时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText = "解析出错";
            }
        }

        // ===== 更新方法 =====

        /// <summary>手动检查更新</summary>
        private async void CheckUpdate()
        {
            StatusText = "正在检查更新...";
            try
            {
                var app = (App)Application.Current;
                await app.CheckForUpdateAsync(silent: false);
                StatusText = "更新检查完成";
            }
            catch (Exception ex)
            {
                StatusText = "检查更新失败";
                MessageBox.Show($"检查更新时出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== BT 专用方法 =====

        /// <summary>选择本地 .torrent 种子文件</summary>
        private void SelectTorrentFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择种子文件",
                Filter = "种子文件 (*.torrent)|*.torrent|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                var fileName = dialog.FileName;
                var result = MessageBox.Show(
                    $"种子文件: {Path.GetFileName(fileName)}\n\n保存到: {_configManager.Config.DefaultSavePath}\n\n确定添加吗？",
                    "添加 BT 下载任务",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _taskManager.AddTorrentTask(fileName, _configManager.Config.DefaultSavePath, _configManager.Config.DefaultSpeedLimit);
                    StatusText = "BT 任务已添加";
                }
            }
        }

        /// <summary>显示 Tracker 配置窗口</summary>
        private void ShowTrackerConfig()
        {
            var window = new Views.TrackerConfigWindow(_taskManager.TrackerConfig);
            window.ShowDialog();
        }

        /// <summary>显示选中 BT 任务的详细信息</summary>
        private async void ShowTorrentDetail()
        {
            if (SelectedTask == null) return;

            var trackerStatus = await _taskManager.GetTrackerStatusAsync(SelectedTask.Id);

            var detail = $"文件名: {SelectedTask.FileName}\n" +
                         $"类型: {SelectedTask.Type}\n" +
                         $"状态: {SelectedTask.StatusText}\n" +
                         $"进度: {SelectedTask.Progress}%\n" +
                         $"大小: {(SelectedTask.TotalSize > 0 ? $"{SelectedTask.TotalSize / 1024.0 / 1024.0:F2} MB" : "未知")}\n" +
                         $"已下载: {(SelectedTask.DownloadedSize > 0 ? $"{SelectedTask.DownloadedSize / 1024.0 / 1024.0:F2} MB" : "0")}\n" +
                         $"下载速度: {SelectedTask.SpeedText}\n" +
                         $"种子数: {SelectedTask.Seeds}\n" +
                         $"用户数: {SelectedTask.Peers}\n" +
                         (SelectedTask.InfoHash.Length > 0 ? $"InfoHash: {SelectedTask.InfoHash}\n" : "") +
                         $"\nTracker 列表 ({trackerStatus.Count}):\n" +
                         string.Join("\n", trackerStatus.Select(t => $"  {t.Url} [{t.Status}] {t.LastAnnounce}"));

            MessageBox.Show(detail, "BT 任务详情", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateStats()
        {
            TotalTasks = Tasks.Count;
            ActiveTasks = Tasks.Count(t => t.Status == DownloadStatus.Downloading);
            CompletedTasks = Tasks.Count(t => t.Status == DownloadStatus.Completed);

            var btCount = Tasks.Count(t => t.Type == DownloadType.Magnet || t.Type == DownloadType.Torrent);
            StatusText = $"总计: {TotalTasks} | 下载中: {ActiveTasks} | 已完成: {CompletedTasks}" +
                         (btCount > 0 ? $" | BT: {btCount}" : "");
        }

        public void Dispose()
        {
            _taskManager?.Dispose();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
