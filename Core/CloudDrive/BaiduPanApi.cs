using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Core.CloudDrive
{
    /// <summary>
    /// 百度网盘 API 实现
    /// 基于百度网盘开放平台 REST API
    /// </summary>
    public class BaiduPanApi : ICloudDriveApi
    {
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://pan.baidu.com/rest/2.0/xpan";
        private const string OauthUrl = "https://openapi.baidu.com/oauth/2.0";

        public CloudDriveType DriveType => CloudDriveType.BaiduPan;
        public string Name => "百度网盘";

        public BaiduPanApi()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "pan.baidu.com");
        }

        public string GetAuthUrl(string clientId, string redirectUri, string state = "")
        {
            var url = $"{OauthUrl}/authorize?" +
                      $"response_type=code&" +
                      $"client_id={Uri.EscapeDataString(clientId)}&" +
                      $"redirect_uri={Uri.EscapeDataString(redirectUri)}&" +
                      $"scope=basic,netdisk&" +
                      $"display=popup";
            if (!string.IsNullOrEmpty(state))
                url += $"&state={Uri.EscapeDataString(state)}";
            return url;
        }

        public async Task<TokenResult> GetTokenByCodeAsync(string code, string clientId, string clientSecret, string redirectUri)
        {
            var url = $"{OauthUrl}/token?grant_type=authorization_code&" +
                      $"code={Uri.EscapeDataString(code)}&" +
                      $"client_id={Uri.EscapeDataString(clientId)}&" +
                      $"client_secret={Uri.EscapeDataString(clientSecret)}&" +
                      $"redirect_uri={Uri.EscapeDataString(redirectUri)}";

            var response = await _httpClient.GetAsync(url);
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
            var url = $"{OauthUrl}/token?grant_type=refresh_token&" +
                      $"refresh_token={Uri.EscapeDataString(refreshToken)}&" +
                      $"client_id={Uri.EscapeDataString(clientId)}&" +
                      $"client_secret={Uri.EscapeDataString(clientSecret)}";

            var response = await _httpClient.GetAsync(url);
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
            var url = $"{BaseUrl}/nas?method=uinfo&access_token={Uri.EscapeDataString(accessToken)}";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["errno"]?.GetValue<int>() != 0)
            {
                return new UserInfoResult
                {
                    Success = false,
                    ErrorMessage = $"获取用户信息失败: errno={node?["errno"]?.GetValue<int>()}"
                };
            }

            return new UserInfoResult
            {
                Success = true,
                UserName = node?["baidu_name"]?.GetValue<string>() ?? "",
                UserId = node?["uk"]?.GetValue<long>().ToString() ?? "",
                TotalSpace = node?["total"]?.GetValue<long>() ?? 0,
                UsedSpace = node?["used"]?.GetValue<long>() ?? 0
            };
        }

        public async Task<List<CloudDriveFileInfo>> GetFileListAsync(string accessToken, string parentFileId = "root")
        {
            var dir = parentFileId == "root" ? "/" : parentFileId;
            var url = $"{BaseUrl}/file?method=list&access_token={Uri.EscapeDataString(accessToken)}&" +
                      $"dir={Uri.EscapeDataString(dir)}&order=time&desc=1&limit=1000";

            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            var files = new List<CloudDriveFileInfo>();
            var list = node?["list"]?.AsArray();
            if (list == null) return files;

            foreach (var item in list)
            {
                files.Add(new CloudDriveFileInfo
                {
                    FileId = item?["path"]?.GetValue<string>() ?? "",
                    FileName = item?["server_filename"]?.GetValue<string>() ?? "",
                    FileSize = item?["size"]?.GetValue<long>() ?? 0,
                    FilePath = item?["path"]?.GetValue<string>() ?? "",
                    IsFolder = item?["isdir"]?.GetValue<int>() == 1,
                    Md5 = item?["md5"]?.GetValue<string>() ?? "",
                    CreateTime = DateTimeOffset.FromUnixTimeSeconds(item?["server_ctime"]?.GetValue<long>() ?? 0).DateTime,
                    ModifyTime = DateTimeOffset.FromUnixTimeSeconds(item?["server_mtime"]?.GetValue<long>() ?? 0).DateTime
                });
            }

            return files;
        }

        public async Task<string> GetDownloadUrlAsync(string accessToken, string fileId)
        {
            // fileId 在百度网盘中是文件路径
            var url = $"{BaseUrl}/multimedia?method=filemetas&access_token={Uri.EscapeDataString(accessToken)}&" +
                      $"dlink=1&path={Uri.EscapeDataString($"[{fileId}]")}";

            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();
            var node = JsonNode.Parse(json);

            if (node?["errno"]?.GetValue<int>() != 0)
                throw new Exception($"获取下载链接失败: errno={node?["errno"]?.GetValue<int>()}");

            var list = node?["list"]?.AsArray();
            if (list == null || list.Count == 0)
                throw new Exception("未找到文件下载链接");

            var dlink = list[0]?["dlink"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(dlink))
                throw new Exception("下载链接为空");

            return dlink;
        }

        public async Task<CloudDriveShareInfo> ParseShareLinkAsync(string shareUrl, string? passcode = null)
        {
            // 解析百度网盘分享链接
            // 格式: https://pan.baidu.com/s/1xxxxx 或 https://pan.baidu.com/share/init?surl=xxxxx
            var result = new CloudDriveShareInfo();

            try
            {
                var uri = new Uri(shareUrl);
                var surl = "";

                if (uri.AbsolutePath.StartsWith("/s/"))
                {
                    surl = uri.AbsolutePath.Substring(3);
                }
                else if (uri.AbsolutePath == "/share/init")
                {
                    var query = HttpUtility.ParseQueryString(uri.Query);
                    surl = query["surl"] ?? "";
                }

                if (string.IsNullOrEmpty(surl))
                {
                    result.ErrorMessage = "无法解析分享链接格式";
                    return result;
                }

                result.ShareId = surl;
                result.Passcode = passcode ?? "";
                result.Success = true;

                // 如果有提取码，需要先验证
                if (!string.IsNullOrEmpty(passcode))
                {
                    var verifyUrl = "https://pan.baidu.com/share/verify";
                    var content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["surl"] = surl,
                        ["pwd"] = passcode
                    });

                    var response = await _httpClient.PostAsync(verifyUrl, content);
                    var json = await response.Content.ReadAsStringAsync();
                    var node = JsonNode.Parse(json);

                    if (node?["errno"]?.GetValue<int>() != 0)
                    {
                        result.Success = false;
                        result.ErrorMessage = "提取码错误或分享已失效";
                        return result;
                    }

                    // 从响应头或Cookie中获取shareToken
                    if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                    {
                        foreach (var cookie in cookies)
                        {
                            if (cookie.Contains("BDCLND="))
                            {
                                result.ShareToken = cookie.Split("BDCLND=")[1].Split(';')[0];
                                break;
                            }
                        }
                    }
                }
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
            var uk = "";
            var seckey = "";

            try
            {
                // 获取分享页面信息
                var shareUrl = $"https://pan.baidu.com/s/1{shareId}";
                var response = await _httpClient.GetAsync(shareUrl);
                var html = await response.Content.ReadAsStringAsync();

                // 从HTML中提取 uk 和 seckey
                // 简化实现：实际需要通过正则或更复杂的方式提取
                var ukMatch = System.Text.RegularExpressions.Regex.Match(html, "\"uk\":(\\d+)");
                if (ukMatch.Success) uk = ukMatch.Groups[1].Value;

                var seckeyMatch = System.Text.RegularExpressions.Regex.Match(html, "\"seckey\":\"([^\"]+)\"");
                if (seckeyMatch.Success) seckey = seckeyMatch.Groups[1].Value;

                // 获取文件列表
                var listUrl = "https://pan.baidu.com/share/list";
                var query = $"uk={uk}&shareid={shareId}&page=1&num=1000&order=time&desc=1&seckey={Uri.EscapeDataString(seckey)}";
                if (!string.IsNullOrEmpty(parentFileId))
                    query += $"&dir={Uri.EscapeDataString(parentFileId)}";

                var listResponse = await _httpClient.GetAsync($"{listUrl}?{query}");
                var json = await listResponse.Content.ReadAsStringAsync();
                var node = JsonNode.Parse(json);

                if (node?["errno"]?.GetValue<int>() != 0)
                    return files;

                var list = node?["list"]?.AsArray();
                if (list == null) return files;

                foreach (var item in list)
                {
                    files.Add(new CloudDriveFileInfo
                    {
                        FileId = item?["path"]?.GetValue<string>() ?? "",
                        FileName = item?["server_filename"]?.GetValue<string>() ?? "",
                        FileSize = item?["size"]?.GetValue<long>() ?? 0,
                        FilePath = item?["path"]?.GetValue<string>() ?? "",
                        IsFolder = item?["isdir"]?.GetValue<int>() == 1,
                        Md5 = item?["md5"]?.GetValue<string>() ?? ""
                    });
                }
            }
            catch { }

            return files;
        }

        public async Task<string> GetShareDownloadUrlAsync(string shareId, string shareToken, string fileId, string? accessToken = null)
        {
            // 百度网盘分享文件下载需要先保存到自己的网盘，或者使用特殊接口
            // 这里使用 yun.139.com 的转存后下载方式简化实现
            // 实际实现需要更复杂的逻辑

            if (string.IsNullOrEmpty(accessToken))
                throw new Exception("下载分享文件需要登录百度网盘账号");

            // 先转存文件
            var transferUrl = $"{BaseUrl}/share/transfer?access_token={Uri.EscapeDataString(accessToken)}";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["shareid"] = shareId,
                ["from"] = shareToken, // uk
                ["sekey"] = shareToken,
                ["path"] = "/apps/IsparkDownloader",
                ["filelist"] = $"[{fileId}]"
            });

            var response = await _httpClient.PostAsync(transferUrl, content);
            var json = await response.Content.ReadAsStringAsync();

            // 转存成功后获取下载链接
            return await GetDownloadUrlAsync(accessToken, $"/apps/IsparkDownloader/{Path.GetFileName(fileId)}");
        }
    }
}
