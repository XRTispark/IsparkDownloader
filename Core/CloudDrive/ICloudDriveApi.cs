using IsparkDownloader2.Models;

namespace IsparkDownloader2.Core.CloudDrive
{
    /// <summary>
    /// 网盘 API 接口定义
    /// </summary>
    public interface ICloudDriveApi
    {
        CloudDriveType DriveType { get; }
        string Name { get; }

        /// <summary>获取 OAuth2 授权 URL</summary>
        string GetAuthUrl(string clientId, string redirectUri, string state = "");

        /// <summary>通过授权码获取 AccessToken</summary>
        Task<TokenResult> GetTokenByCodeAsync(string code, string clientId, string clientSecret, string redirectUri);

        /// <summary>刷新 AccessToken</summary>
        Task<TokenResult> RefreshTokenAsync(string refreshToken, string clientId, string clientSecret);

        /// <summary>获取用户信息</summary>
        Task<UserInfoResult> GetUserInfoAsync(string accessToken);

        /// <summary>获取网盘根目录文件列表</summary>
        Task<List<CloudDriveFileInfo>> GetFileListAsync(string accessToken, string parentFileId = "root");

        /// <summary>获取文件下载直链</summary>
        Task<string> GetDownloadUrlAsync(string accessToken, string fileId);

        /// <summary>解析分享链接</summary>
        Task<CloudDriveShareInfo> ParseShareLinkAsync(string shareUrl, string? passcode = null);

        /// <summary>获取分享链接中的文件列表</summary>
        Task<List<CloudDriveFileInfo>> GetShareFileListAsync(string shareId, string shareToken, string? parentFileId = null);

        /// <summary>获取分享文件的下载直链</summary>
        Task<string> GetShareDownloadUrlAsync(string shareId, string shareToken, string fileId, string? accessToken = null);
    }

    public class TokenResult
    {
        public bool Success { get; set; }
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class UserInfoResult
    {
        public bool Success { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public long TotalSpace { get; set; }
        public long UsedSpace { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
