using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsparkDownloader2.Core
{
    /// <summary>
    /// 浏览器模拟器 - 模拟真实浏览器行为以防止403拒绝
    /// </summary>
    public class BrowserSimulator
    {
        private readonly string _dataPath;
        private readonly CookieContainer _cookieContainer;
        private Dictionary<string, string> _localStorage = new();
        private Dictionary<string, string> _sessionStorage = new();
        private readonly Random _random = new();
        private bool _dataLoaded;

        public BrowserProfile Profile { get; set; } = new();

        public CookieContainer CookieContainer { get { EnsureDataLoaded(); return _cookieContainer; } }
        public IReadOnlyDictionary<string, string> LocalStorage { get { EnsureDataLoaded(); return _localStorage; } }
        public IReadOnlyDictionary<string, string> SessionStorage => _sessionStorage;

        public BrowserSimulator()
        {
            _dataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IsparkDownloader2",
                "browser"
            );
            _cookieContainer = new CookieContainer();
            // 延迟加载，首次需要时才读取磁盘
        }

        /// <summary>
        /// 确保数据已加载
        /// </summary>
        private void EnsureDataLoaded()
        {
            if (!_dataLoaded)
            {
                _dataLoaded = true;
                LoadData();
            }
        }

        /// <summary>
        /// 创建带浏览器模拟的 HttpClient
        /// </summary>
        public HttpClient CreateHttpClient(int timeoutSeconds = 30)
        {
            var handler = new SocketsHttpHandler
            {
                MaxConnectionsPerServer = 20,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                EnableMultipleHttp2Connections = true,
                CookieContainer = _cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            ApplyHeaders(client);
            return client;
        }

        /// <summary>
        /// 应用浏览器请求头
        /// </summary>
        public void ApplyHeaders(HttpClient client)
        {
            client.DefaultRequestHeaders.Clear();

            // User-Agent
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                string.IsNullOrEmpty(Profile.UserAgent)
                    ? GetRandomUserAgent()
                    : Profile.UserAgent);

            // Accept
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");

            // Accept-Language
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language",
                string.IsNullOrEmpty(Profile.AcceptLanguage)
                    ? "zh-CN,zh;q=0.9,en;q=0.8"
                    : Profile.AcceptLanguage);

            // Accept-Encoding
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");

            // Referer
            if (!string.IsNullOrEmpty(Profile.Referer))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Profile.Referer);
            }

            // DNT
            if (Profile.DoNotTrack)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("DNT", "1");
            }

            // Sec headers (Chrome style)
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua",
                "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            // 自定义请求头
            foreach (var header in Profile.CustomHeaders)
            {
                if (!client.DefaultRequestHeaders.Contains(header.Key))
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        /// <summary>
        /// 为特定请求应用浏览器模拟头
        /// </summary>
        public void ApplyToRequest(HttpRequestMessage request, string? referer = null)
        {
            request.Headers.TryAddWithoutValidation("User-Agent",
                string.IsNullOrEmpty(Profile.UserAgent) ? GetRandomUserAgent() : Profile.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language",
                string.IsNullOrEmpty(Profile.AcceptLanguage) ? "zh-CN,zh;q=0.9,en;q=0.8" : Profile.AcceptLanguage);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");

            if (!string.IsNullOrEmpty(referer))
            {
                request.Headers.TryAddWithoutValidation("Referer", referer);
            }
            else if (!string.IsNullOrEmpty(Profile.Referer))
            {
                request.Headers.TryAddWithoutValidation("Referer", Profile.Referer);
            }

            request.Headers.TryAddWithoutValidation("sec-ch-ua",
                "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
            request.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");

            foreach (var header in Profile.CustomHeaders)
            {
                if (!request.Headers.Contains(header.Key))
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        /// <summary>
        /// 添加/更新 Cookie
        /// </summary>
        public void SetCookie(string url, string name, string value, DateTime? expires = null)
        {
            var uri = new Uri(url);
            var cookie = new Cookie(name, value, "/", uri.Host)
            {
                Expires = expires ?? DateTime.Now.AddDays(30)
            };
            _cookieContainer.Add(uri, cookie);
            SaveData();
        }

        /// <summary>
        /// 获取 Cookie
        /// </summary>
        public string? GetCookie(string url, string name)
        {
            var uri = new Uri(url);
            var cookies = _cookieContainer.GetCookies(uri);
            return cookies[name]?.Value;
        }

        /// <summary>
        /// 获取所有 Cookie 字符串
        /// </summary>
        public string GetCookieString(string url)
        {
            var uri = new Uri(url);
            var cookies = _cookieContainer.GetCookies(uri);
            return string.Join("; ", cookies.Cast<Cookie>().Select(c => $"{c.Name}={c.Value}"));
        }

        /// <summary>
        /// 删除 Cookie
        /// </summary>
        public void RemoveCookie(string url, string name)
        {
            var uri = new Uri(url);
            var cookie = new Cookie(name, "", "/", uri.Host) { Expires = DateTime.Now.AddDays(-1) };
            _cookieContainer.Add(uri, cookie);
            SaveData();
        }

        /// <summary>
        /// 清除所有 Cookie
        /// </summary>
        public void ClearCookies()
        {
            _cookieContainer.GetType().GetProperty("PerDomainCapacity")?.SetValue(_cookieContainer, 0);
            SaveData();
        }

        /// <summary>
        /// 设置 LocalStorage
        /// </summary>
        public void SetLocalStorage(string key, string value)
        {
            _localStorage[key] = value;
            SaveData();
        }

        /// <summary>
        /// 获取 LocalStorage
        /// </summary>
        public string? GetLocalStorage(string key)
        {
            return _localStorage.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// 删除 LocalStorage
        /// </summary>
        public void RemoveLocalStorage(string key)
        {
            _localStorage.Remove(key);
            SaveData();
        }

        /// <summary>
        /// 设置 SessionStorage
        /// </summary>
        public void SetSessionStorage(string key, string value)
        {
            _sessionStorage[key] = value;
        }

        /// <summary>
        /// 获取 SessionStorage
        /// </summary>
        public string? GetSessionStorage(string key)
        {
            return _sessionStorage.TryGetValue(key, out var value) ? value : null;
        }

        /// <summary>
        /// 清除 SessionStorage
        /// </summary>
        public void ClearSessionStorage()
        {
            _sessionStorage.Clear();
        }

        /// <summary>
        /// 模拟 JavaScript 指纹
        /// </summary>
        public string GenerateFingerprintScript()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Browser fingerprint simulation");
            sb.AppendLine($"navigator.userAgent = '{Profile.UserAgent?.Replace("'", "\\'") ?? GetRandomUserAgent()}';");
            sb.AppendLine($"navigator.language = '{Profile.AcceptLanguage?.Split(',')[0] ?? "zh-CN"}';");
            sb.AppendLine($"navigator.platform = '{Profile.Platform ?? "Win32"}';");
            sb.AppendLine($"navigator.hardwareConcurrency = {Profile.HardwareConcurrency ?? 8};");
            sb.AppendLine($"screen.width = {Profile.ScreenWidth ?? 1920};");
            sb.AppendLine($"screen.height = {Profile.ScreenHeight ?? 1080};");
            sb.AppendLine($"screen.colorDepth = {Profile.ColorDepth ?? 24};");
            sb.AppendLine($"window.devicePixelRatio = {Profile.DevicePixelRatio ?? 1.0};");

            // LocalStorage
            foreach (var item in _localStorage)
            {
                sb.AppendLine($"localStorage.setItem('{item.Key.Replace("'", "\\'")}', '{item.Value.Replace("'", "\\'")}');");
            }

            // SessionStorage
            foreach (var item in _sessionStorage)
            {
                sb.AppendLine($"sessionStorage.setItem('{item.Key.Replace("'", "\\'")}', '{item.Value.Replace("'", "\\'")}');");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 获取随机 User-Agent
        /// </summary>
        public static string GetRandomUserAgent()
        {
            var agents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36 Edg/119.0.0.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:120.0) Gecko/20100101 Firefox/120.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.1 Safari/605.1.15",
                "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            };
            return agents[new Random().Next(agents.Length)];
        }

        /// <summary>
        /// 随机化指纹（防检测）
        /// </summary>
        public void RandomizeFingerprint()
        {
            Profile.UserAgent = GetRandomUserAgent();
            Profile.ScreenWidth = new[] { 1920, 1366, 1440, 1536, 1680, 2560 }[_random.Next(6)];
            Profile.ScreenHeight = new[] { 1080, 768, 900, 864, 1050, 1440 }[_random.Next(6)];
            Profile.HardwareConcurrency = new[] { 4, 6, 8, 12, 16 }[_random.Next(5)];
            Profile.DevicePixelRatio = new[] { 1.0, 1.25, 1.5, 2.0 }[_random.Next(4)];
        }

        private void LoadData()
        {
            try
            {
                Directory.CreateDirectory(_dataPath);

                // Load cookies
                var cookiePath = Path.Combine(_dataPath, "cookies.json");
                if (File.Exists(cookiePath))
                {
                    var json = File.ReadAllText(cookiePath);
                    var cookies = JsonConvert.DeserializeObject<List<SerializableCookie>>(json);
                    if (cookies != null)
                    {
                        foreach (var c in cookies)
                        {
                            try
                            {
                                var cookie = new Cookie(c.Name, c.Value, c.Path, c.Domain)
                                {
                                    Expires = c.Expires
                                };
                                _cookieContainer.Add(new Uri($"https://{c.Domain}"), cookie);
                            }
                            catch { }
                        }
                    }
                }

                // Load localStorage
                var lsPath = Path.Combine(_dataPath, "localStorage.json");
                if (File.Exists(lsPath))
                {
                    var json = File.ReadAllText(lsPath);
                    _localStorage = JsonConvert.DeserializeObject<Dictionary<string, string>>(json) ?? new();
                }

                // Load profile
                var profilePath = Path.Combine(_dataPath, "profile.json");
                if (File.Exists(profilePath))
                {
                    var json = File.ReadAllText(profilePath);
                    Profile = JsonConvert.DeserializeObject<BrowserProfile>(json) ?? new BrowserProfile();
                }
            }
            catch { }
        }

        public void SaveData()
        {
            try
            {
                Directory.CreateDirectory(_dataPath);

                // Save cookies
                var cookies = new List<SerializableCookie>();
                foreach (Cookie cookie in _cookieContainer.GetAllCookies())
                {
                    cookies.Add(new SerializableCookie
                    {
                        Name = cookie.Name,
                        Value = cookie.Value,
                        Domain = cookie.Domain,
                        Path = cookie.Path,
                        Expires = cookie.Expires
                    });
                }
                File.WriteAllText(Path.Combine(_dataPath, "cookies.json"),
                    JsonConvert.SerializeObject(cookies, Formatting.Indented));

                // Save localStorage
                File.WriteAllText(Path.Combine(_dataPath, "localStorage.json"),
                    JsonConvert.SerializeObject(_localStorage, Formatting.Indented));

                // Save profile
                File.WriteAllText(Path.Combine(_dataPath, "profile.json"),
                    JsonConvert.SerializeObject(Profile, Formatting.Indented));
            }
            catch { }
        }

        private class SerializableCookie
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
            public string Domain { get; set; } = "";
            public string Path { get; set; } = "/";
            public DateTime Expires { get; set; }
        }
    }

    /// <summary>
    /// 浏览器指纹配置
    /// </summary>
    public class BrowserProfile
    {
        public string UserAgent { get; set; } =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        public string AcceptLanguage { get; set; } = "zh-CN,zh;q=0.9,en;q=0.8";
        public string? Referer { get; set; }
        public bool DoNotTrack { get; set; } = false;
        public string Platform { get; set; } = "Win32";
        public int? HardwareConcurrency { get; set; } = 8;
        public int? ScreenWidth { get; set; } = 1920;
        public int? ScreenHeight { get; set; } = 1080;
        public int? ColorDepth { get; set; } = 24;
        public double? DevicePixelRatio { get; set; } = 1.0;
        public Dictionary<string, string> CustomHeaders { get; set; } = new();
        public bool EnableSimulation { get; set; } = true;
    }
}
