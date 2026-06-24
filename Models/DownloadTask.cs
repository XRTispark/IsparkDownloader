using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace IsparkDownloader2.Models
{
    public enum DownloadStatus
    {
        Pending,
        Downloading,
        Paused,
        Completed,
        Failed,
        Cancelled
    }

    public enum DownloadType
    {
        Http,
        Https,
        Ftp,
        Magnet,
        Torrent,
        CloudDrive
    }

    public class DownloadTask : INotifyPropertyChanged
    {
        private string _id = Guid.NewGuid().ToString("N");
        private string _url = string.Empty;
        private string _fileName = string.Empty;
        private string _savePath = string.Empty;
        private long _totalSize = 0;
        private long _downloadedSize = 0;
        private DownloadStatus _status = DownloadStatus.Pending;
        private double _speed = 0;
        private int _progress = 0;
        private string _errorMessage = string.Empty;
        private DateTime _createTime = DateTime.Now;
        private DateTime? _completeTime;
        private int _threadCount = 8;
        private long _speedLimit = 0;
        private DownloadType _type = DownloadType.Http;
        // BT 特有字段
        private int _seeds = 0;
        private int _peers = 0;
        private double _uploadSpeed = 0;
        private string _infoHash = string.Empty;
        private string _torrentFilePath = string.Empty;
        private string _torrentName = string.Empty;

        // 详情字段（用于右键详情窗口）
        private int _segmentCount = 0;
        private int _completedSegments = 0;
        private int _activeConnections = 0;
        private string _userAgent = string.Empty;
        private string _referer = string.Empty;
        private int _cookieCount = 0;
        private int _localStorageCount = 0;
        private string _proxyUrl = string.Empty;
        private bool _useBrowserSimulation = false;
        private string _fileType = string.Empty;
        private string _serverInfo = string.Empty;
        private DateTime? _startTime;
        private TimeSpan? _elapsedTime;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Url
        {
            get => _url;
            set { _url = value; OnPropertyChanged(); }
        }

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public string SavePath
        {
            get => _savePath;
            set { _savePath = value; OnPropertyChanged(); }
        }

        public string FullPath => Path.Combine(_savePath, _fileName);

        public long TotalSize
        {
            get => _totalSize;
            set
            {
                _totalSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeText));
                UpdateProgress();
            }
        }

        public long DownloadedSize
        {
            get => _downloadedSize;
            set
            {
                _downloadedSize = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SizeText));
                UpdateProgress();
            }
        }

        public DownloadStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public double Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedText));
            }
        }

        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public DateTime CreateTime
        {
            get => _createTime;
            set { _createTime = value; OnPropertyChanged(); }
        }

        public DateTime? CompleteTime
        {
            get => _completeTime;
            set { _completeTime = value; OnPropertyChanged(); }
        }

        public int ThreadCount
        {
            get => _threadCount;
            set { _threadCount = value; OnPropertyChanged(); }
        }

        public long SpeedLimit
        {
            get => _speedLimit;
            set { _speedLimit = value; OnPropertyChanged(); }
        }

        public DownloadType Type
        {
            get => _type;
            set { _type = value; OnPropertyChanged(); }
        }

        // ========== BT 特有属性 ==========

        public int Seeds
        {
            get => _seeds;
            set { _seeds = value; OnPropertyChanged(); }
        }

        public int Peers
        {
            get => _peers;
            set { _peers = value; OnPropertyChanged(); }
        }

        public double UploadSpeed
        {
            get => _uploadSpeed;
            set { _uploadSpeed = value; OnPropertyChanged(); }
        }

        public string InfoHash
        {
            get => _infoHash;
            set { _infoHash = value; OnPropertyChanged(); }
        }

        public string TorrentFilePath
        {
            get => _torrentFilePath;
            set { _torrentFilePath = value; OnPropertyChanged(); }
        }

        public string TorrentName
        {
            get => _torrentName;
            set { _torrentName = value; OnPropertyChanged(); }
        }

        // ========== 详情属性 ==========

        public int SegmentCount
        {
            get => _segmentCount;
            set { _segmentCount = value; OnPropertyChanged(); }
        }

        public int CompletedSegments
        {
            get => _completedSegments;
            set { _completedSegments = value; OnPropertyChanged(); }
        }

        public int ActiveConnections
        {
            get => _activeConnections;
            set { _activeConnections = value; OnPropertyChanged(); }
        }

        public string UserAgent
        {
            get => _userAgent;
            set { _userAgent = value; OnPropertyChanged(); }
        }

        public string Referer
        {
            get => _referer;
            set { _referer = value; OnPropertyChanged(); }
        }

        public int CookieCount
        {
            get => _cookieCount;
            set { _cookieCount = value; OnPropertyChanged(); }
        }

        public int LocalStorageCount
        {
            get => _localStorageCount;
            set { _localStorageCount = value; OnPropertyChanged(); }
        }

        public string ProxyUrl
        {
            get => _proxyUrl;
            set { _proxyUrl = value; OnPropertyChanged(); }
        }

        public bool UseBrowserSimulation
        {
            get => _useBrowserSimulation;
            set { _useBrowserSimulation = value; OnPropertyChanged(); }
        }

        public string FileType
        {
            get => _fileType;
            set { _fileType = value; OnPropertyChanged(); }
        }

        public string ServerInfo
        {
            get => _serverInfo;
            set { _serverInfo = value; OnPropertyChanged(); }
        }

        public DateTime? StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(); }
        }

        public TimeSpan? ElapsedTime
        {
            get => _elapsedTime;
            set { _elapsedTime = value; OnPropertyChanged(); }
        }

        public string SeedsPeersText => $"S: {_seeds} P: {_peers}";

        public string InfoHashShort => _infoHash.Length > 16
            ? _infoHash[..16] + "..."
            : _infoHash;

        public string StatusText => Status switch
        {
            DownloadStatus.Pending => "等待中",
            DownloadStatus.Downloading => "下载中",
            DownloadStatus.Paused => "已暂停",
            DownloadStatus.Completed => "已完成",
            DownloadStatus.Failed => "失败",
            DownloadStatus.Cancelled => "已取消",
            _ => "未知"
        };

        public string SpeedText => Speed > 1024 * 1024
            ? $"{Speed / 1024 / 1024:F2} MB/s"
            : $"{Speed / 1024:F2} KB/s";

        public string SizeText => TotalSize > 1024 * 1024 * 1024
            ? $"{DownloadedSize / 1024.0 / 1024.0 / 1024.0:F2} / {TotalSize / 1024.0 / 1024.0 / 1024.0:F2} GB"
            : $"{DownloadedSize / 1024.0 / 1024.0:F2} / {TotalSize / 1024.0 / 1024.0:F2} MB";

        private void UpdateProgress()
        {
            if (TotalSize > 0)
            {
                Progress = (int)((double)DownloadedSize / TotalSize * 100);
            }
            else
            {
                Progress = 0;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
