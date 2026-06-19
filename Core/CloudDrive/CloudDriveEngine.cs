using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Core.CloudDrive
{
    /// <summary>
    /// 网盘下载引擎
    /// 通过网盘 API 获取直链后，复用 HTTP 下载引擎进行下载
    /// </summary>
    public class CloudDriveEngine : IDisposable
    {
        private readonly DownloadEngine _httpEngine;
        private readonly Dictionary<CloudDriveType, ICloudDriveApi> _apis;
        private readonly CloudDriveConfigManager _configManager;
        private readonly HttpClient _httpClient;
        private bool _disposed;

        public CloudDriveConfigManager ConfigManager => _configManager;

        public event EventHandler<DownloadProgressEventArgs>? ProgressChanged
        {
            add => _httpEngine.ProgressChanged += value;
            remove => _httpEngine.ProgressChanged -= value;
        }

        public event EventHandler? DownloadCompleted
        {
            add => _httpEngine.DownloadCompleted += value;
            remove => _httpEngine.DownloadCompleted -= value;
        }

        public event EventHandler<string>? DownloadFailed
        {
            add => _httpEngine.DownloadFailed += value;
            remove => _httpEngine.DownloadFailed -= value;
        }

        public CloudDriveEngine(CloudDriveConfigManager configManager)
        {
            _configManager = configManager;
            _httpEngine = new DownloadEngine();
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

            _apis = new Dictionary<CloudDriveType, ICloudDriveApi>
            {
                [CloudDriveType.BaiduPan] = new BaiduPanApi(),
                [CloudDriveType.AliyunDrive] = new AliyunDriveApi(),
                [CloudDriveType.QuarkDrive] = new QuarkDriveApi()
            };
        }

        /// <summary>
        /// 获取所有支持的网盘类型
        /// </summary>
        public IReadOnlyDictionary<CloudDriveType, ICloudDriveApi> SupportedApis => _apis;

        /// <summary>
        /// 解析网盘分享链接，自动识别网盘类型
        /// </summary>
        public async Task<(CloudDriveType? driveType, CloudDriveShareInfo shareInfo)> ParseShareLinkAsync(string shareUrl, string? passcode = null)
        {
            // 根据 URL 域名识别网盘类型
            CloudDriveType? driveType = IdentifyDriveType(shareUrl);
            if (driveType == null)
                return (null, new CloudDriveShareInfo { ErrorMessage = "无法识别的网盘分享链接" });

            var api = _apis[driveType.Value];
            var shareInfo = await api.ParseShareLinkAsync(shareUrl, passcode);
            return (driveType, shareInfo);
        }

        /// <summary>
        /// 获取分享链接中的文件列表
        /// </summary>
        public async Task<List<CloudDriveFileInfo>> GetShareFileListAsync(CloudDriveType driveType, string shareId, string shareToken, string? parentFileId = null)
        {
            var api = _apis[driveType];
            return await api.GetShareFileListAsync(shareId, shareToken, parentFileId);
        }

        /// <summary>
        /// 下载分享链接中的文件
        /// </summary>
        public async Task<bool> DownloadShareFileAsync(DownloadTask task, CloudDriveType driveType, string shareId, string shareToken, string fileId)
        {
            try
            {
                var api = _apis[driveType];

                // 获取账号 Token（如果需要）
                var account = _configManager.GetAccount(driveType);
                string? accessToken = account?.AccessToken;

                // 获取下载直链
                var downloadUrl = await api.GetShareDownloadUrlAsync(shareId, shareToken, fileId, accessToken);

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    task.Status = DownloadStatus.Failed;
                    task.ErrorMessage = "无法获取文件下载链接";
                    return false;
                }

                // 设置下载 URL 并复用 HTTP 引擎下载
                task.Url = downloadUrl;

                // 对于百度网盘，需要在请求头中添加 AccessToken
                if (driveType == CloudDriveType.BaiduPan && account != null)
                {
                    return await DownloadWithAuthAsync(task, downloadUrl, accessToken!);
                }

                return await _httpEngine.DownloadAsync(task);
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                task.ErrorMessage = $"网盘下载失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 下载网盘个人空间中的文件
        /// </summary>
        public async Task<bool> DownloadPersonalFileAsync(DownloadTask task, CloudDriveType driveType, string fileId)
        {
            try
            {
                var api = _apis[driveType];
                var account = _configManager.GetAccount(driveType);

                if (account == null || !account.IsLoggedIn)
                {
                    task.Status = DownloadStatus.Failed;
                    task.ErrorMessage = $"未登录 {api.Name} 账号，请先登录";
                    return false;
                }

                // 检查 Token 是否过期，尝试刷新
                if (DateTime.Now >= account.TokenExpireTime && !string.IsNullOrEmpty(account.RefreshToken))
                {
                    var refreshResult = await api.RefreshTokenAsync(account.RefreshToken, "", "");
                    if (refreshResult.Success)
                    {
                        account.AccessToken = refreshResult.AccessToken;
                        account.RefreshToken = refreshResult.RefreshToken;
                        account.TokenExpireTime = DateTime.Now.AddSeconds(refreshResult.ExpiresIn);
                        _configManager.UpdateAccount(account);
                    }
                }

                // 获取下载直链
                var downloadUrl = await api.GetDownloadUrlAsync(account.AccessToken, fileId);

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    task.Status = DownloadStatus.Failed;
                    task.ErrorMessage = "无法获取文件下载链接";
                    return false;
                }

                task.Url = downloadUrl;

                // 百度网盘需要认证头
                if (driveType == CloudDriveType.BaiduPan)
                {
                    return await DownloadWithAuthAsync(task, downloadUrl, account.AccessToken);
                }

                return await _httpEngine.DownloadAsync(task);
            }
            catch (Exception ex)
            {
                task.Status = DownloadStatus.Failed;
                task.ErrorMessage = $"网盘下载失败: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// 获取网盘文件列表（个人空间）
        /// </summary>
        public async Task<List<CloudDriveFileInfo>> GetPersonalFileListAsync(CloudDriveType driveType, string? parentFileId = null)
        {
            var api = _apis[driveType];
            var account = _configManager.GetAccount(driveType);

            if (account == null || !account.IsLoggedIn)
                return new List<CloudDriveFileInfo>();

            return await api.GetFileListAsync(account.AccessToken, parentFileId ?? "root");
        }

        /// <summary>
        /// 登录网盘账号（OAuth2 授权码方式）
        /// </summary>
        public async Task<bool> LoginAsync(CloudDriveType driveType, string clientId, string clientSecret, string authCode, string redirectUri)
        {
            try
            {
                var api = _apis[driveType];
                var tokenResult = await api.GetTokenByCodeAsync(authCode, clientId, clientSecret, redirectUri);

                if (!tokenResult.Success)
                    return false;

                var userInfo = await api.GetUserInfoAsync(tokenResult.AccessToken);

                var account = new CloudDriveAccount
                {
                    DriveType = driveType,
                    Name = api.Name,
                    AccessToken = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken,
                    TokenExpireTime = DateTime.Now.AddSeconds(tokenResult.ExpiresIn),
                    IsLoggedIn = true,
                    UserName = userInfo.UserName,
                    TotalSpace = userInfo.TotalSpace,
                    UsedSpace = userInfo.UsedSpace
                };

                // 如果已存在同类型账号，先移除
                var existing = _configManager.GetAccount(driveType);
                if (existing != null)
                    _configManager.RemoveAccount(existing.Id);

                _configManager.AddAccount(account);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 使用 Token 直接登录（适用于夸克网盘等 Cookie 方式）
        /// </summary>
        public async Task<bool> LoginWithTokenAsync(CloudDriveType driveType, string token)
        {
            try
            {
                var api = _apis[driveType];
                var userInfo = await api.GetUserInfoAsync(token);

                if (!userInfo.Success)
                    return false;

                var account = new CloudDriveAccount
                {
                    DriveType = driveType,
                    Name = api.Name,
                    AccessToken = token,
                    IsLoggedIn = true,
                    UserName = userInfo.UserName,
                    TotalSpace = userInfo.TotalSpace,
                    UsedSpace = userInfo.UsedSpace
                };

                var existing = _configManager.GetAccount(driveType);
                if (existing != null)
                    _configManager.RemoveAccount(existing.Id);

                _configManager.AddAccount(account);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取 OAuth2 授权 URL
        /// </summary>
        public string GetAuthUrl(CloudDriveType driveType, string clientId, string redirectUri)
        {
            return _apis[driveType].GetAuthUrl(clientId, redirectUri, Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// 登出网盘账号
        /// </summary>
        public void Logout(CloudDriveType driveType)
        {
            var account = _configManager.GetAccount(driveType);
            if (account != null)
            {
                account.IsLoggedIn = false;
                account.AccessToken = "";
                account.RefreshToken = "";
                _configManager.UpdateAccount(account);
            }
        }

        public void PauseDownload(string taskId)
        {
            _httpEngine.PauseDownload(taskId);
        }

        public void CancelDownload(string taskId)
        {
            _httpEngine.CancelDownload(taskId);
        }

        /// <summary>
        /// 识别网盘类型
        /// </summary>
        private CloudDriveType? IdentifyDriveType(string url)
        {
            var lower = url.ToLowerInvariant();
            if (lower.Contains("pan.baidu.com") || lower.Contains("yun.139.com"))
                return CloudDriveType.BaiduPan;
            if (lower.Contains("alipan.com") || lower.Contains("aliyundrive.com"))
                return CloudDriveType.AliyunDrive;
            if (lower.Contains("pan.quark.cn") || lower.Contains("quark.cn"))
                return CloudDriveType.QuarkDrive;
            return null;
        }

        /// <summary>
        /// 使用认证信息下载（百度网盘需要）
        /// </summary>
        private async Task<bool> DownloadWithAuthAsync(DownloadTask task, string url, string accessToken)
        {
            var cts = new CancellationTokenSource();
            try
            {
                task.Status = DownloadStatus.Downloading;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "pan.baidu.com");

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                if (totalBytes > 0) task.TotalSize = totalBytes;

                Directory.CreateDirectory(task.SavePath);
                var tempPath = task.FullPath + ".tmp";

                await using var contentStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                long downloadedBytes = 0;
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var lastReportTime = DateTime.Now;
                long lastReportBytes = 0;

                while (true)
                {
                    var read = await contentStream.ReadAsync(buffer);
                    if (read == 0) break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    downloadedBytes += read;
                    task.DownloadedSize = downloadedBytes;

                    if (task.SpeedLimit > 0)
                    {
                        var elapsed = stopwatch.Elapsed.TotalSeconds;
                        var expectedTime = downloadedBytes / (double)task.SpeedLimit;
                        if (expectedTime > elapsed)
                            await Task.Delay((int)((expectedTime - elapsed) * 1000));
                    }

                    var now = DateTime.Now;
                    if ((now - lastReportTime).TotalMilliseconds >= 500)
                    {
                        var timeDiff = (now - lastReportTime).TotalSeconds;
                        var bytesDiff = downloadedBytes - lastReportBytes;
                        task.Speed = bytesDiff / timeDiff;
                        lastReportTime = now;
                        lastReportBytes = downloadedBytes;
                    }
                }

                fileStream.Close();

                if (File.Exists(task.FullPath))
                    File.Delete(task.FullPath);
                File.Move(tempPath, task.FullPath);

                task.Status = DownloadStatus.Completed;
                task.CompleteTime = DateTime.Now;
                task.Speed = 0;
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
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpEngine?.Dispose();
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
