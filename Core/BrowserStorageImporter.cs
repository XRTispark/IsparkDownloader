using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace IsparkDownloader2.Core
{
    /// <summary>
    /// 自动从本地浏览器导入 Cookie、LocalStorage 等数据
    /// </summary>
    public class BrowserStorageImporter
    {
        private readonly string _localAppData;

        public BrowserStorageImporter()
        {
            _localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        /// <summary>
        /// 获取所有可检测到的浏览器
        /// </summary>
        public List<DetectedBrowser> DetectBrowsers()
        {
            var browsers = new List<DetectedBrowser>();

            var paths = new (string Name, BrowserType Type, string Path)[]
            {
                ("Google Chrome", BrowserType.Chrome, Path.Combine(_localAppData, "Google", "Chrome", "User Data")),
                ("Microsoft Edge", BrowserType.Edge, Path.Combine(_localAppData, "Microsoft", "Edge", "User Data")),
            };

            foreach (var (name, type, path) in paths)
            {
                if (Directory.Exists(path))
                    browsers.Add(new DetectedBrowser { Name = name, Type = type, ProfilePath = path });
            }

            // 360 安全浏览器（多个可能路径）
            var qihuPaths = new[]
            {
                Path.Combine(_localAppData, "360ChromeX", "Chrome", "User Data"),
                Path.Combine(_localAppData, "360Chrome", "Chrome", "User Data"),
            };
            foreach (var p in qihuPaths)
            {
                if (Directory.Exists(p))
                {
                    browsers.Add(new DetectedBrowser { Name = "360 安全浏览器", Type = BrowserType.Chrome, ProfilePath = p });
                    break;
                }
            }

            // QQ 浏览器
            var qqPath = Path.Combine(_localAppData, "Tencent", "QQBrowser", "User Data");
            if (Directory.Exists(qqPath))
                browsers.Add(new DetectedBrowser { Name = "QQ 浏览器", Type = BrowserType.Chrome, ProfilePath = qqPath });

            // Firefox
            var firefoxPath = Path.Combine(_localAppData, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(firefoxPath))
            {
                foreach (var profile in Directory.GetDirectories(firefoxPath))
                {
                    browsers.Add(new DetectedBrowser
                    {
                        Name = $"Firefox ({Path.GetFileName(profile)})",
                        Type = BrowserType.Firefox,
                        ProfilePath = profile
                    });
                }
            }

            return browsers;
        }

        /// <summary>
        /// 从 Chrome/Edge 导入 Cookie
        /// </summary>
        public List<ImportedCookie> ImportChromiumCookies(string profilePath, string? domainFilter = null)
        {
            var cookies = new List<ImportedCookie>();
            var cookieDbPath = FindChromiumCookieDb(profilePath);
            if (string.IsNullOrEmpty(cookieDbPath))
                return cookies;

            var masterKey = GetChromiumMasterKey(profilePath);
            var usedTempFile = false;
            var tempDb = "";

            // 策略1：直接用 SQLite 打开原文件（只读模式）
            // 策略2：复制到临时文件再打开
            Microsoft.Data.Sqlite.SqliteConnection? connection = null;
            try
            {
                connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={cookieDbPath};Mode=ReadOnly");
                connection.Open();
            }
            catch
            {
                // 直接打开失败，尝试复制
                tempDb = Path.Combine(Path.GetTempPath(), $"ispark_cookies_{Guid.NewGuid()}.db");
                try
                {
                    File.Copy(cookieDbPath, tempDb, true);
                    connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}");
                    connection.Open();
                    usedTempFile = true;
                }
                catch
                {
                    return cookies;
                }
            }

            try
            {
                // 验证 cookies 表存在
                using var tableCmd = new Microsoft.Data.Sqlite.SqliteCommand(
                    "SELECT name FROM sqlite_master WHERE type='table' AND name='cookies'", connection);
                if (tableCmd.ExecuteScalar() == null)
                    return cookies;

                var query = "SELECT host_key, name, value, encrypted_value, path, expires_utc, is_secure, is_httponly FROM cookies";
                if (!string.IsNullOrEmpty(domainFilter))
                    query += " WHERE host_key LIKE @domain";

                using var command = new Microsoft.Data.Sqlite.SqliteCommand(query, connection);
                if (!string.IsNullOrEmpty(domainFilter))
                    command.Parameters.AddWithValue("@domain", $"%{domainFilter}%");

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    try
                    {
                        var hostKey = reader.GetString(0);
                        var name = reader.GetString(1);
                        var path = reader.IsDBNull(4) ? "/" : reader.GetString(4);
                        var expiresUtc = reader.IsDBNull(5) ? 0 : reader.GetInt64(5);
                        var isSecure = !reader.IsDBNull(6) && reader.GetInt32(6) == 1;

                        var expires = expiresUtc > 0
                            ? DateTime.FromFileTimeUtc(expiresUtc * 10).ToLocalTime()
                            : DateTime.Now.AddDays(30);

                        // 获取 Cookie 值
                        string cookieValue = "";
                        var plainValue = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        if (!string.IsNullOrEmpty(plainValue))
                        {
                            cookieValue = plainValue;
                        }
                        else
                        {
                            var encryptedValue = reader.IsDBNull(3) ? null : (byte[])reader[3];
                            if (encryptedValue != null && encryptedValue.Length > 0)
                            {
                                if (masterKey != null)
                                    cookieValue = DecryptChromiumCookie(encryptedValue, masterKey);
                                // 即使解密失败，也记录 Cookie 的元信息
                                if (string.IsNullOrEmpty(cookieValue))
                                    cookieValue = $"[加密-{encryptedValue.Length}字节]";
                            }
                        }

                        cookies.Add(new ImportedCookie
                        {
                            Domain = hostKey,
                            Name = name,
                            Value = cookieValue,
                            Path = path,
                            Expires = expires,
                            IsSecure = isSecure
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取 Cookie 数据库失败: {ex.Message}");
            }
            finally
            {
                connection?.Close();
                connection?.Dispose();
                if (usedTempFile)
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }

            return cookies;
        }

        /// <summary>
        /// 查找 Chromium Cookie 数据库路径
        /// </summary>
        private string? FindChromiumCookieDb(string profilePath)
        {
            // 搜索所有 profile 目录
            var profileDirs = new[] { "Default" };
            try
            {
                profileDirs = profileDirs.Concat(
                    Directory.GetDirectories(profilePath, "Profile *").Select(Path.GetFileName)
                ).ToArray();
            }
            catch { }

            foreach (var dir in profileDirs)
            {
                var paths = new[]
                {
                    Path.Combine(profilePath, dir, "Network", "Cookies"),
                    Path.Combine(profilePath, dir, "Cookies"),
                };
                foreach (var p in paths)
                {
                    if (File.Exists(p)) return p;
                }
            }

            return null;
        }

        /// <summary>
        /// 获取 Chromium 主密钥
        /// </summary>
        private byte[]? GetChromiumMasterKey(string profilePath)
        {
            try
            {
                var localStatePath = Path.Combine(profilePath, "Local State");
                if (!File.Exists(localStatePath))
                    return null;

                var json = File.ReadAllText(localStatePath);
                var jObject = JObject.Parse(json);
                var osCrypt = jObject["os_crypt"] as JObject;
                var encryptedKey = osCrypt?["encrypted_key"]?.ToString();
                if (string.IsNullOrEmpty(encryptedKey))
                    return null;

                var keyBytes = Convert.FromBase64String(encryptedKey);
                var dpapiBytes = keyBytes[5..];
                var decrypted = ProtectedData.Unprotect(dpapiBytes, null, DataProtectionScope.CurrentUser);
                return decrypted;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取 Chromium 主密钥失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 解密 Chromium Cookie (AES-256-GCM)
        /// </summary>
        private string DecryptChromiumCookie(byte[] encryptedValue, byte[] masterKey)
        {
            try
            {
                if (encryptedValue.Length < 3)
                    return "";

                var prefix = Encoding.UTF8.GetString(encryptedValue[..3]);
                if (prefix != "v10" && prefix != "v11")
                {
                    var decrypted = ProtectedData.Unprotect(encryptedValue, null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(decrypted);
                }

                var nonce = new byte[12];
                Buffer.BlockCopy(encryptedValue, 3, nonce, 0, 12);

                var ciphertext = new byte[encryptedValue.Length - 31];
                Buffer.BlockCopy(encryptedValue, 15, ciphertext, 0, ciphertext.Length);

                var tag = new byte[16];
                Buffer.BlockCopy(encryptedValue, encryptedValue.Length - 16, tag, 0, 16);

                var decryptedBytes = new byte[ciphertext.Length];
                using var aes = new AesGcm(masterKey, 16);
                aes.Decrypt(nonce, ciphertext, tag, decryptedBytes);

                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"解密 Cookie 失败: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// 从 Chrome/Edge 导入 LocalStorage
        /// Chrome 的 Local Storage 存储在 leveldb 格式的 .ldb 文件中
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> ImportChromiumLocalStorage(string profilePath)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();

            // Chrome Local Storage 使用 SQLite 格式存储在 Local Storage/leveldb 下
            var lsDbPath = Path.Combine(profilePath, "Default", "Local Storage", "leveldb");
            if (!Directory.Exists(lsDbPath))
            {
                lsDbPath = Path.Combine(profilePath, "Default", "Local Storage");
            }

            if (!Directory.Exists(lsDbPath))
                return result;

            // Chrome 使用 .ldb (Sorted String Table) 和 .log (Write Ahead Log) 文件
            // 数据格式为: key(前缀 _https://xxx: ) + \x00 + value
            try
            {
                foreach (var file in Directory.GetFiles(lsDbPath, "*.ldb")
                    .Concat(Directory.GetFiles(lsDbPath, "*.log")))
                {
                    try
                    {
                        var content = File.ReadAllBytes(file);

                        // 查找所有 _https:// 开头的 key（Chrome LocalStorage key 格式）
                        var text = Encoding.UTF8.GetString(content);
                        // Chrome 格式: _https://domain\x00key\x00value 或 _https://domain\x01key\x00value
                        var pattern = @"_(https?://[^\x00-\x08]+)\x00([^\x00-\x08]+)\x00(.{1,50000})";
                        var matches = Regex.Matches(text, pattern);

                        foreach (Match match in matches)
                        {
                            var domain = match.Groups[1].Value;
                            var key = match.Groups[2].Value;
                            var value = match.Groups[3].Value;

                            // 跳过太长的值（可能是二进制数据）
                            if (value.Length > 50000)
                                continue;

                            if (!result.ContainsKey(domain))
                                result[domain] = new Dictionary<string, string>();

                            result[domain][key] = value;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导入 LocalStorage 失败: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// 从 Firefox 导入 Cookie
        /// </summary>
        public List<ImportedCookie> ImportFirefoxCookies(string profilePath, string? domainFilter = null)
        {
            var cookies = new List<ImportedCookie>();
            var cookieDbPath = Path.Combine(profilePath, "cookies.sqlite");
            if (!File.Exists(cookieDbPath))
                return cookies;

            Microsoft.Data.Sqlite.SqliteConnection? connection = null;
            var usedTempFile = false;
            var tempDb = "";

            try
            {
                connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={cookieDbPath};Mode=ReadOnly");
                connection.Open();
            }
            catch
            {
                tempDb = Path.Combine(Path.GetTempPath(), $"ispark_ff_cookies_{Guid.NewGuid()}.sqlite");
                try
                {
                    File.Copy(cookieDbPath, tempDb, true);
                    connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}");
                    connection.Open();
                    usedTempFile = true;
                }
                catch
                {
                    return cookies;
                }
            }

            try
            {
                var query = "SELECT host, name, value, path, expiry, isSecure FROM moz_cookies";
                if (!string.IsNullOrEmpty(domainFilter))
                    query += " WHERE host LIKE @domain";

                using var command = new Microsoft.Data.Sqlite.SqliteCommand(query, connection);
                if (!string.IsNullOrEmpty(domainFilter))
                    command.Parameters.AddWithValue("@domain", $"%{domainFilter}%");

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    try
                    {
                        var expiry = reader.GetInt64(4);
                        var expires = expiry > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(expiry).LocalDateTime
                            : DateTime.Now.AddDays(30);

                        cookies.Add(new ImportedCookie
                        {
                            Domain = reader.GetString(0),
                            Name = reader.GetString(1),
                            Value = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Path = reader.IsDBNull(3) ? "/" : reader.GetString(3),
                            Expires = expires,
                            IsSecure = !reader.IsDBNull(5) && reader.GetInt32(5) == 1
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导入 Firefox Cookie 失败: {ex.Message}");
            }
            finally
            {
                connection?.Close();
                connection?.Dispose();
                if (usedTempFile)
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }

            return cookies;
        }

        /// <summary>
        /// 从 Firefox 导入 LocalStorage
        /// </summary>
        public Dictionary<string, Dictionary<string, string>> ImportFirefoxLocalStorage(string profilePath)
        {
            var result = new Dictionary<string, Dictionary<string, string>>();
            var storagePath = Path.Combine(profilePath, "webappsstore.sqlite");
            if (!File.Exists(storagePath))
                return result;

            Microsoft.Data.Sqlite.SqliteConnection? connection = null;
            var usedTempFile = false;
            var tempDb = "";

            try
            {
                connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={storagePath};Mode=ReadOnly");
                connection.Open();
            }
            catch
            {
                tempDb = Path.Combine(Path.GetTempPath(), $"ispark_ff_ls_{Guid.NewGuid()}.sqlite");
                try
                {
                    File.Copy(storagePath, tempDb, true);
                    connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}");
                    connection.Open();
                    usedTempFile = true;
                }
                catch
                {
                    return result;
                }
            }

            try
            {
                using var command = new Microsoft.Data.Sqlite.SqliteCommand(
                    "SELECT scope, key, value FROM webappsstore2", connection);
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    try
                    {
                        var scope = reader.GetString(0);
                        var key = reader.GetString(1);
                        var value = reader.IsDBNull(2) ? "" : reader.GetString(2);
                        var domain = scope.Split('^')[0];

                        if (!result.ContainsKey(domain))
                            result[domain] = new Dictionary<string, string>();

                        result[domain][key] = value;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导入 Firefox LocalStorage 失败: {ex.Message}");
            }
            finally
            {
                connection?.Close();
                connection?.Dispose();
                if (usedTempFile)
                {
                    try { File.Delete(tempDb); } catch { }
                }
            }

            return result;
        }

        /// <summary>
        /// 从 JSON 文件导入 Cookie（Chrome Cookie-Editor 格式）
        /// </summary>
        public List<ImportedCookie> ImportFromJson(string jsonContent)
        {
            var cookies = new List<ImportedCookie>();
            try
            {
                var array = JArray.Parse(jsonContent);
                foreach (var item in array)
                {
                    try
                    {
                        cookies.Add(new ImportedCookie
                        {
                            Domain = item["domain"]?.ToString() ?? "",
                            Name = item["name"]?.ToString() ?? "",
                            Value = item["value"]?.ToString() ?? "",
                            Path = item["path"]?.ToString() ?? "/",
                            Expires = item["expirationDate"] != null
                                ? DateTimeOffset.FromUnixTimeSeconds((long)item["expirationDate"]).LocalDateTime
                                : DateTime.Now.AddDays(30),
                            IsSecure = item["secure"]?.ToObject<bool>() ?? false
                        });
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"导入 JSON Cookie 失败: {ex.Message}");
            }
            return cookies;
        }

        /// <summary>
        /// 从 Netscape cookies.txt 格式导入
        /// </summary>
        public List<ImportedCookie> ImportFromNetscape(string content)
        {
            var cookies = new List<ImportedCookie>();
            var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split('\t');
                if (parts.Length >= 7)
                {
                    try
                    {
                        var expires = long.TryParse(parts[4], out var exp) && exp > 0
                            ? DateTimeOffset.FromUnixTimeSeconds(exp).LocalDateTime
                            : DateTime.Now.AddDays(30);

                        cookies.Add(new ImportedCookie
                        {
                            Domain = parts[0],
                            Name = parts[5],
                            Value = parts[6],
                            Path = parts[2],
                            Expires = expires,
                            IsSecure = parts[3] == "TRUE"
                        });
                    }
                    catch { }
                }
            }

            return cookies;
        }

        /// <summary>
        /// 导入所有浏览器的 Cookie 到 BrowserSimulator
        /// </summary>
        public ImportResult ImportAllCookies(BrowserSimulator simulator, string? domainFilter = null)
        {
            var result = new ImportResult();
            var browsers = DetectBrowsers();

            foreach (var browser in browsers)
            {
                try
                {
                    List<ImportedCookie> cookies;
                    if (browser.Type == BrowserType.Firefox)
                        cookies = ImportFirefoxCookies(browser.ProfilePath, domainFilter);
                    else
                        cookies = ImportChromiumCookies(browser.ProfilePath, domainFilter);

                    foreach (var cookie in cookies)
                    {
                        try
                        {
                            var url = $"https://{cookie.Domain.TrimStart('.')}";
                            simulator.SetCookie(url, cookie.Name, cookie.Value, cookie.Expires);
                            result.CookieCount++;
                        }
                        catch { }
                    }

                    result.BrowsersProcessed++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{browser.Name}: {ex.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// 导入所有浏览器的 LocalStorage 到 BrowserSimulator
        /// </summary>
        public ImportResult ImportAllLocalStorage(BrowserSimulator simulator, string? domainFilter = null)
        {
            var result = new ImportResult();
            var browsers = DetectBrowsers();

            foreach (var browser in browsers)
            {
                try
                {
                    Dictionary<string, Dictionary<string, string>> storage;
                    if (browser.Type == BrowserType.Firefox)
                        storage = ImportFirefoxLocalStorage(browser.ProfilePath);
                    else
                        storage = ImportChromiumLocalStorage(browser.ProfilePath);

                    foreach (var domain in storage.Keys)
                    {
                        if (!string.IsNullOrEmpty(domainFilter) && !domain.Contains(domainFilter))
                            continue;

                        foreach (var item in storage[domain])
                        {
                            try
                            {
                                var key = $"{domain}:{item.Key}";
                                simulator.SetLocalStorage(key, item.Value);
                                result.LocalStorageCount++;
                            }
                            catch { }
                        }
                    }

                    result.BrowsersProcessed++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{browser.Name}: {ex.Message}");
                }
            }

            return result;
        }
    }

    public class DetectedBrowser
    {
        public string Name { get; set; } = "";
        public BrowserType Type { get; set; }
        public string ProfilePath { get; set; } = "";
    }

    public enum BrowserType { Chrome, Edge, Firefox }

    public class ImportedCookie
    {
        public string Domain { get; set; } = "";
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Path { get; set; } = "/";
        public DateTime Expires { get; set; }
        public bool IsSecure { get; set; }
    }

    public class ImportResult
    {
        public int CookieCount { get; set; }
        public int LocalStorageCount { get; set; }
        public int BrowsersProcessed { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
