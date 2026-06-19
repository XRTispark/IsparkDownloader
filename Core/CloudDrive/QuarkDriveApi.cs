using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Core.CloudDrive
{
    /// <summary>
    /// 夸克网盘 API 实现
    /// 基于夸克网盘 Web API（夸克未提供官方开放平台，使用 Web 端接口）
    /// </summary>
    public class QuarkDriveApi : ICloudDriveApi
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://drive.quark.cn/1/clouddrive";
        private const string AuthUrl = "https://openapi.alipan.com/oauth/authorize"; // 夸克使用阿里系OAuth

        public CloudDriveType DriveType => CloudDriveType.QuarkDrive;
        public string Name => "夸克网盘";

        public QuarkDriveApi()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public string GetAuthUrl(string clientId, string redirectUri, string state = "")
        {
            // 夸克网盘目前主要通过 Cookie/Token 方式登录
            // 这里返回一个说明，引导用户手动获取 Token
            return "https://pan.quark.cn/";
        }

        public async Task<TokenResult> GetTokenByCodeAsync(string code, string clientId, string clientSecret, string redirectUri)
        {
            // 夸克网盘需要通过浏览器获取 Cookie 中的 token
            // 简化实现：返回需要手动输入的提示
            return new TokenResult
            {
                Success = false,
                ErrorMessage = "夸克网盘需要通过浏览器登录后手动获取 Cookie 中的 ctoken 和 puid"
            };
        }

        public async Task<TokenResult> RefreshTokenAsync(string refreshToken, string clientId, string clientSecret)
        {
            return new TokenResult
            {
                Success = false,
                ErrorMessage = "夸克网盘暂不支持自动刷新 Token，请重新获取 Cookie"
            };
        }

        public async Task<UserInfoResult> GetUserInfoAsync(string accessToken)
        {
            // accessToken 在夸克网盘中是 ctoken
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/user/login?pr=ucpro&fr=pc&_t={DateTimeOffset.Now.ToUnixTimeSeconds()}");
            request.Headers.Add("Cookie", $"ctoken={accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["code"]?.GetValue<int>() != 0)
            {
                return new UserInfoResult
                {
                    Success = false,
                    ErrorMessage = node?["message"]?.GetValue<string>() ?? "获取用户信息失败"
                };
            }

            var data = node?["data"] ?? node;
            return new UserInfoResult
            {
                Success = true,
                UserName = data?["nick_name"]?.GetValue<string>() ?? data?["phone"]?.GetValue<string>() ?? "",
                UserId = data?["user_id"]?.GetValue<string>() ?? "",
                TotalSpace = data?["total_capacity"]?.GetValue<long>() ?? 0,
                UsedSpace = data?["used_capacity"]?.GetValue<long>() ?? 0
            };
        }

        public async Task<List<CloudDriveFileInfo>> GetFileListAsync(string accessToken, string parentFileId = "root")
        {
            var pid = parentFileId == "root" ? "0" : parentFileId;
            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/file/sort?pr=ucpro&fr=pc&pdir_fid={pid}&_page=1&_size=100&_fetch_total=1&_sort=file_type:asc,updated_at:desc&_t={DateTimeOffset.Now.ToUnixTimeSeconds()}");
            request.Headers.Add("Cookie", $"ctoken={accessToken}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            var files = new List<CloudDriveFileInfo>();
            var dataList = node?["data"]?.AsArray()?["list"]?.AsArray();
            if (dataList == null) return files;

            foreach (var item in dataList)
            {
                files.Add(new CloudDriveFileInfo
                {
                    FileId = item?["fid"]?.GetValue<string>() ?? "",
                    FileName = item?["file_name"]?.GetValue<string>() ?? "",
                    FileSize = item?["size"]?.GetValue<long>() ?? 0,
                    FilePath = item?["file_path"]?.GetValue<string>() ?? "",
                    IsFolder = item?["file_type"]?.GetValue<int>() == 0,
                    CreateTime = DateTimeOffset.FromUnixTimeSeconds(item?["created_at"]?.GetValue<long>() ?? 0).DateTime,
                    ModifyTime = DateTimeOffset.FromUnixTimeSeconds(item?["updated_at"]?.GetValue<long>() ?? 0).DateTime
                });
            }

            return files;
        }

        public async Task<string> GetDownloadUrlAsync(string accessToken, string fileId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/file/download?pr=ucpro&fr=pc&_t={DateTimeOffset.Now.ToUnixTimeSeconds()}");
            request.Headers.Add("Cookie", $"ctoken={accessToken}");
            request.Content = new StringContent($"{{\"fids\":[{fileId}]}}", Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["code"]?.GetValue<int>() != 0)
                throw new Exception($"获取下载链接失败: {node?["message"]?.GetValue<string>()}");

            var data = node?["data"]?.AsArray();
            if (data == null || data.Count == 0)
                throw new Exception("未找到文件下载链接");

            var url = data[0]?["download_url"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(url))
                url = data[0]?["url"]?.GetValue<string>() ?? "";

            if (string.IsNullOrEmpty(url))
                throw new Exception("下载链接为空");

            return url;
        }

        public async Task<CloudDriveShareInfo> ParseShareLinkAsync(string shareUrl, string? passcode = null)
        {
            var result = new CloudDriveShareInfo();

            try
            {
                // 夸克网盘分享链接格式: https://pan.quark.cn/s/xxxxx
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

                // 获取分享页面信息
                var response = await _httpClient.GetAsync(shareUrl);
                var html = await response.Content.ReadAsStringAsync();

                // 从HTML中提取 stoken
                var stokenMatch = System.Text.RegularExpressions.Regex.Match(html, @"""stoken""\s*:\s*""([^""]+)""");
                if (stokenMatch.Success)
                {
                    result.ShareToken = stokenMatch.Groups[1].Value;
                }

                // 如果有提取码，需要验证
                if (!string.IsNullOrEmpty(passcode))
                {
                    var verifyRequest = new HttpRequestMessage(HttpMethod.Post,
                        $"{BaseUrl}/share/sharepage/token?pr=ucpro&fr=pc");
                    verifyRequest.Content = new StringContent(
                        $"{{\"share_id\":\"{shareId}\",\"passcode\":\"{passcode}\"}}",
                        Encoding.UTF8, "application/json");

                    var verifyResponse = await _httpClient.SendAsync(verifyRequest);
                    var verifyJson = await verifyResponse.Content.ReadAsStringAsync();
                    var verifyNode = JsonNode.Parse(verifyJson);

                    if (verifyNode?["code"]?.GetValue<int>() != 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "提取码错误或分享已失效";
                        return result;
                    }

                    result.ShareToken = verifyNode?["data"]?.GetValue<string>() ?? result.ShareToken;
                }

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
                var pid = parentFileId ?? "0";
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"{BaseUrl}/share/sharepage/detail?pr=ucpro&fr=pc&share_id={shareId}&stoken={Uri.EscapeDataString(shareToken)}&pwd_id={shareId}&_page=1&_size=100&_fetch_total=1&_sort=file_type:asc,updated_at:desc&pdir_fid={pid}&_t={DateTimeOffset.Now.ToUnixTimeSeconds()}");

                var response = await _httpClient.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);

                if (node?["code"]?.GetValue<int>() != 0)
                    return files;

                var dataList = node?["data"]?.AsArray()?["list"]?.AsArray();
                if (dataList == null) return files;

                foreach (var item in dataList)
                {
                    files.Add(new CloudDriveFileInfo
                    {
                        FileId = item?["fid"]?.GetValue<string>() ?? "",
                        FileName = item?["file_name"]?.GetValue<string>() ?? "",
                        FileSize = item?["size"]?.GetValue<long>() ?? 0,
                        FilePath = item?["file_path"]?.GetValue<string>() ?? "",
                        IsFolder = item?["file_type"]?.GetValue<int>() == 0
                    });
                }
            }
            catch { }

            return files;
        }

        public async Task<string> GetShareDownloadUrlAsync(string shareId, string shareToken, string fileId, string? accessToken = null)
        {
            // 分享文件下载需要先保存到自己的网盘
            if (string.IsNullOrEmpty(accessToken))
                throw new Exception("下载分享文件需要登录夸克网盘账号");

            // 1. 先保存文件到自己的网盘
            var saveRequest = new HttpRequestMessage(HttpMethod.Post,
                $"{BaseUrl}/share/sharepage/save?pr=ucpro&fr=pc&_t={DateTimeOffset.Now.ToUnixTimeSeconds()}");
            saveRequest.Headers.Add("Cookie", $"ctoken={accessToken}");
            saveRequest.Content = new StringContent(
                $"{{\"share_id\":\"{shareId}\",\"stoken\":\"{shareToken}\",\"fid_list\":[{fileId}],\"fid_token_list\":[],\"to_pdir_fid\":0}}",
                Encoding.UTF8, "application/json");

            var saveResponse = await _httpClient.SendAsync(saveRequest);
            var saveJson = await saveResponse.Content.ReadAsStringAsync();
            var saveNode = JsonNode.Parse(saveJson);

            if (saveNode?["code"]?.GetValue<int>() != 0)
                throw new Exception($"保存文件失败: {saveNode?["message"]?.GetValue<string>()}");

            // 2. 获取保存后的文件ID并下载
            var newFid = saveNode?["data"]?.AsArray()?["fid_list"]?.AsArray()?[0]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(newFid))
                throw new Exception("无法获取保存后的文件ID");

            return await GetDownloadUrlAsync(accessToken, newFid);
        }
    }
}
