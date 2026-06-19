namespace IsparkDownloader2.Models
{
    /// <summary>
    /// 网盘类型枚举
    /// </summary>
    public enum CloudDriveType
    {
        BaiduPan,      // 百度网盘
        AliyunDrive,   // 阿里云盘
        QuarkDrive     // 夸克网盘
    }

    /// <summary>
    /// 网盘文件信息
    /// </summary>
    public class CloudDriveFileInfo
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public string Md5 { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public DateTime CreateTime { get; set; }
        public DateTime ModifyTime { get; set; }
    }

    /// <summary>
    /// 网盘账号配置
    /// </summary>
    public class CloudDriveAccount
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public CloudDriveType DriveType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime TokenExpireTime { get; set; }
        public bool IsLoggedIn { get; set; }
        public string UserName { get; set; } = string.Empty;
        public long TotalSpace { get; set; }
        public long UsedSpace { get; set; }
    }

    /// <summary>
    /// 网盘分享链接解析结果
    /// </summary>
    public class CloudDriveShareInfo
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ShareId { get; set; } = string.Empty;
        public string ShareToken { get; set; } = string.Empty;
        public string Passcode { get; set; } = string.Empty;
        public List<CloudDriveFileInfo> Files { get; set; } = new();
    }
}
