using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IsparkDownloader2.Core
{
    public class AppConfig
    {
        public string DefaultSavePath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        public int DefaultThreadCount { get; set; } = 8;
        public int MaxConcurrentDownloads { get; set; } = 3;
        public long DefaultSpeedLimit { get; set; } = 0;
        public bool AutoStartDownload { get; set; } = true;
        public bool ShowNotificationOnComplete { get; set; } = true;
        public bool AutoRemoveCompleted { get; set; } = false;
        public string Theme { get; set; } = "Dark";
        public string Language { get; set; } = "zh-CN";
        public string UserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        public int ConnectionTimeout { get; set; } = 30;
        public int RetryCount { get; set; } = 3;
        public bool EnableProxy { get; set; } = false;
        public string? ProxyAddress { get; set; }
        public int ProxyPort { get; set; } = 8080;
    }

    public class ConfigManager
    {
        private readonly string _configPath;
        private AppConfig _config = new();

        public AppConfig Config => _config;

        public ConfigManager()
        {
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IsparkDownloader2",
                "config.json"
            );
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    _config = new AppConfig();
                    Save();
                }
            }
            catch
            {
                _config = new AppConfig();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                var json = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }

        public T Get<T>(string key, T defaultValue) where T : notnull
        {
            try
            {
                var json = JsonConvert.SerializeObject(_config);
                var obj = JObject.Parse(json);
                var token = obj.SelectToken(key);
                if (token != null)
                {
                    var value = token.ToObject<T>();
                    if (value != null) return value;
                }
                return defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        public void Set<T>(string key, T value)
        {
            try
            {
                var json = JsonConvert.SerializeObject(_config);
                var obj = JObject.Parse(json);
                obj[key] = JToken.FromObject(value!);
                _config = obj.ToObject<AppConfig>() ?? _config;
                Save();
            }
            catch { }
        }
    }
}
