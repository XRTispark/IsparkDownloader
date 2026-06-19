using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Core.CloudDrive
{
    /// <summary>
    /// 阿里云盘 Open API 实现
    /// 基于阿里云盘开放平台官方 API
    /// </summary>
    public class AliyunDriveApi : ICloudDriveApi
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://openapi.alipan.com";
        private const string AuthUrl = "https://openapi.alipan.com/oauth/authorize";
        private const string TokenUrl = "https://openapi.alipan.com/oauth/token";
        private const string ApiUrl = "https://openapi.alipan.com/adrive/v1.0";

        public CloudDriveType DriveType => CloudDriveType.AliyunDrive;
        public string Name => "阿里云盘";

        public AliyunDriveApi()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public string GetAuthUrl(string clientId, string redirectUri, string state = "")
        {
            var url = $"{AuthUrl}?" +
                      $"client_id={Uri.EscapeDataString(clientId)}&" +
                      $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                      $"scope=user:base,file:all:read,file:all:write&" +
                      $"response_type=code";
            if (!string.IsNullOrEmpty(state))
                url += $"&state={Uri.EscapeDataString(state)}";
            return url;
        }

        public async Task<TokenResult> GetTokenByCodeAsync(string code, string clientId, string clientSecret, string redirectUri)
        {
            var requestBody = new
            {
                grant_type = "authorization_code",
                code = code,
                client_id = clientId,
                client_secret = clientSecret,
                redirect_uri = redirectUri
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(TokenUrl, content);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["error"] != null)
            {
                return new TokenResult
                {
                    Success = false,
                    ErrorMessage = node["error_description"]?.GetValue<string>() ?? "获取Token失败"
                };
            }

            return new TokenResult
            {
                Success = true,
                AccessToken = node?["access_token"]?.GetValue<string>() ?? "",
                RefreshToken = node?["refresh_token"]?.GetValue<string>() ?? "",
                ExpiresIn = node?["expires_in"]?.GetValue<int>() ?? 0
            };
        }

        public async Task<TokenResult> RefreshTokenAsync(string refreshToken, string clientId, string clientSecret)
        {
            var requestBody = new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                client_id = clientId,
                client_secret = clientSecret
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(TokenUrl, content);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["error"] != null)
            {
                return new TokenResult
                {
                    Success = false,
                    ErrorMessage = node["error_description"]?.GetValue<string>() ?? "刷新Token失败"
                };
            }

            return new TokenResult
            {
                Success = true,
                AccessToken = node?["access_token"]?.GetValue<string>() ?? "",
                RefreshToken = node?["refresh_token"]?.GetValue<string>() ?? "",
                ExpiresIn = node?["expires_in"]?.GetValue<int>() ?? 0
            };
        }

        public async Task<UserInfoResult> GetUserInfoAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiUrl}/user/get");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["code"] != null && node["code"]?.GetValue<string>() != null)
            {
                return new UserInfoResult
                {
                    Success = false,
                    ErrorMessage = node["message"]?.GetValue<string>() ?? "获取用户信息失败"
                };
            }

            return new UserInfoResult
            {
                Success = true,
                UserName = node?["name"]?.GetValue<string>() ?? "",
                UserId = node?["user_id"]?.GetValue<string>() ?? "",
                TotalSpace = 0, // 阿里云盘需要单独获取空间信息
                UsedSpace = 0
            };
        }

        public async Task<List<CloudDriveFileInfo>> GetFileListAsync(string accessToken, string parentFileId = "root")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiUrl}/openFile/list");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var requestBody = new
            {
                drive_id = await GetDefaultDriveIdAsync(accessToken),
                parent_file_id = parentFileId,
                limit = 100,
                order_by = "updated_at",
                order_direction = "DESC"
            };
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            var files = new List<CloudDriveFileInfo>();
            var items = node?["items"]?.AsArray();
            if (items == null) return files;

            foreach (var item in items)
            {
                files.Add(new CloudDriveFileInfo
                {
                    FileId = item?["file_id"]?.GetValue<string>() ?? "",
                    FileName = item?["name"]?.GetValue<string>() ?? "",
                    FileSize = item?["size"]?.GetValue<long>() ?? 0,
                    FilePath = item?["file_id"]?.GetValue<string>() ?? "",
                    IsFolder = item?["type"]?.GetValue<string>() == "folder",
                    CreateTime = DateTime.Parse(item?["created_at"]?.GetValue<string>() ?? DateTime.MinValue.ToString()),
                    ModifyTime = DateTime.Parse(item?["updated_at"]?.GetValue<string>() ?? DateTime.MinValue.ToString())
                });
            }

            return files;
        }

        public async Task<string> GetDownloadUrlAsync(string accessToken, string fileId)
        {
            var driveId = await GetDefaultDriveIdAsync(accessToken);

            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiUrl}/openFile/getDownloadUrl");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var requestBody = new
            {
                drive_id = driveId,
                file_id = fileId,
                expire_sec = 14400 // 4小时有效期
            };
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["code"] != null)
                throw new Exception($"获取下载链接失败: {node["message"]?.GetValue<string>()}");

            var url = node?["url"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(url))
                throw new Exception("下载链接为空");

            return url;
        }

        public async Task<CloudDriveShareInfo> ParseShareLinkAsync(string shareUrl, string? passcode = null)
        {
            var result = new CloudDriveShareInfo();

            try
            {
                // 阿里云盘分享链接格式: https://www.alipan.com/s/xxxxx
                var uri = new Uri(shareUrl);
                var shareId = "";

                if (uri.AbsolutePath.StartsWith("/s/"))
                {
                    shareId = uri.AbsolutePath.Substring(3).TrimEnd('/');
                }

                if (string.IsNullOrEmpty(shareId))
                {
                    result.ErrorMessage = "无法解析分享链接格式";
                    return result;
                }

                result.ShareId = shareId;
                result.Passcode = passcode ?? "";

                // 获取分享Token
                var tokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/share_link/get_share_token");
                var tokenBody = new
                {
                    share_id = shareId,
                    share_pwd = passcode ?? ""
                };
                tokenRequest.Content = new StringContent(JsonSerializer.Serialize(tokenBody), Encoding.UTF8, "application/json");

                var tokenResponse = await _httpClient.SendAsync(tokenRequest);
                var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                var tokenNode = JsonNode.Parse(tokenJson);

                if (tokenNode?["code"]?.GetValue<string>() != null && tokenNode["code"]?.GetValue<string>() != "OK")
                {
                    result.ErrorMessage = tokenNode["message"]?.GetValue<string>() ?? "分享链接无效或提取码错误";
                    return result;
                }

                result.ShareToken = tokenNode?["share_token"]?.GetValue<string>() ?? "";
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"解析分享链接失败: {ex.Message}";
            }

            return result;
        }

        public async Task<List<CloudDriveFileInfo>> GetShareFileListAsync(string shareId, string shareToken, string? parentFileId = null)
        {
            var files = new List<CloudDriveFileInfo>();

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/adrive/v2/file/list_by_share");
                request.Headers.Add("x-share-token", shareToken);

                var requestBody = new
                {
                    share_id = shareId,
                    parent_file_id = parentFileId ?? "root",
                    limit = 100,
                    order_by = "updated_at",
                    order_direction = "DESC"
                };
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);

                var items = node?["items"]?.AsArray();
                if (items == null) return files;

                foreach (var item in items)
                {
                    files.Add(new CloudDriveFileInfo
                    {
                        FileId = item?["file_id"]?.GetValue<string>() ?? "",
                        FileName = item?["name"]?.GetValue<string>() ?? "",
                        FileSize = item?["size"]?.GetValue<long>() ?? 0,
                        FilePath = item?["file_id"]?.GetValue<string>() ?? "",
                        IsFolder = item?["type"]?.GetValue<string>() == "folder"
                    });
                }
            }
            catch { }

            return files;
        }

        public async Task<string> GetShareDownloadUrlAsync(string shareId, string shareToken, string fileId, string? accessToken = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/file/getShareLinkDownloadUrl");
            request.Headers.Add("x-share-token", shareToken);
            if (!string.IsNullOrEmpty(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var requestBody = new
            {
                share_id = shareId,
                file_id = fileId,
                expire_sec = 14400
            };
            request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["code"] != null)
                throw new Exception($"获取下载链接失败: {node["message"]?.GetValue<string>()}");

            var url = node?["download_url"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(url))
                url = node?["url"]?.GetValue<string>() ?? "";

            if (string.IsNullOrEmpty(url))
                throw new Exception("下载链接为空");

            return url;
        }

        private async Task<string> GetDefaultDriveIdAsync(string accessToken)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiUrl}/user/get");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            return node?["default_drive_id"]?.GetValue<string>() ?? "";
        }
    }
}
