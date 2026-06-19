using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using Newtonsoft.Json;

namespace IsparkDownloader2.Models
{
    /// <summary>
    /// Tracker 状态信息
    /// </summary>
    public class TrackerItem
    {
        public string Url { get; set; } = string.Empty;
        public string Status { get; set; } = "未知";
        public int Seeds { get; set; }
        public int Peers { get; set; }
        public string LastAnnounced { get; set; } = "未连接";
    }

    /// <summary>
    /// Tracker 分类管理
    /// </summary>
    public class TrackerCategory
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Trackers { get; set; } = new();
    }

    /// <summary>
    /// Tracker 配置管理器 - 管理内置和用户自定义的 tracker 列表
    /// </summary>
    public class TrackerConfigManager
    {
        private readonly string _configPath;
        private List<TrackerCategory> _categories = new();
        private List<string> _customTrackers = new();

        public IReadOnlyList<TrackerCategory> Categories => _categories.AsReadOnly();
        public IReadOnlyList<string> CustomTrackers => _customTrackers.AsReadOnly();

        public TrackerConfigManager()
        {
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IsparkDownloader2",
                "trackers.json"
            );
            Load();
        }

        public List<string> GetAllTrackers()
        {
            var all = new List<string>();
            foreach (var category in _categories)
            {
                all.AddRange(category.Trackers);
            }
            all.AddRange(_customTrackers);
            return all.Distinct().ToList();
        }

        public void AddCustomTracker(string url)
        {
            if (!_customTrackers.Contains(url))
            {
                _customTrackers.Add(url);
                Save();
            }
        }

        public void RemoveCustomTracker(string url)
        {
            _customTrackers.Remove(url);
            Save();
        }

        public void AddCategory(string name)
        {
            if (_categories.All(c => c.Name != name))
            {
                _categories.Add(new TrackerCategory { Name = name, Trackers = new List<string>() });
                Save();
            }
        }

        public void AddTrackerToCategory(string categoryName, string url)
        {
            var category = _categories.FirstOrDefault(c => c.Name == categoryName);
            if (category != null && !category.Trackers.Contains(url))
            {
                category.Trackers.Add(url);
                Save();
            }
        }

        public void RemoveTrackerFromCategory(string categoryName, string url)
        {
            var category = _categories.FirstOrDefault(c => c.Name == categoryName);
            if (category != null)
            {
                category.Trackers.Remove(url);
                Save();
            }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                var data = new
                {
                    Version = 2,
                    Categories = _categories,
                    CustomTrackers = _customTrackers
                };
                File.WriteAllText(_configPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch { }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    InitializeDefault();
                    return;
                }

                var json = File.ReadAllText(_configPath);
                var data = JsonConvert.DeserializeAnonymousType(json, new
                {
                    Version = 0,
                    Categories = Array.Empty<TrackerCategory>(),
                    CustomTrackers = Array.Empty<string>()
                });

                if (data?.Categories != null)
                    _categories = data.Categories.ToList();
                if (data?.CustomTrackers != null)
                    _customTrackers = data.CustomTrackers.ToList();

                if (_categories.Count == 0)
                    InitializeDefault();
            }
            catch
            {
                InitializeDefault();
            }
        }

        private void InitializeDefault()
        {
            _categories = new List<TrackerCategory>
            {
                new TrackerCategory
                {
                    Name = "通用 Tracker",
                    Trackers = new List<string>
                    {
                        "udp://tracker.opentrackr.org:1337/announce",
                        "udp://tracker.coppersurfer.tk:6969/announce",
                        "udp://tracker.leechers-paradise.org:6969/announce",
                        "udp://tracker.internetwarriors.net:1337/announce",
                        "udp://tracker.pirateparty.gr:6969/announce",
                        "udp://9.rarbg.com:2710/announce",
                        "udp://tracker.cyberia.is:6969/announce",
                        "udp://exodus.desync.com:6969/announce",
                        "udp://explodie.org:6969/announce",
                        "http://tracker3.itzmx.com:6961/announce",
                        "http://tracker1.itzmx.com:8080/announce",
                        "udp://open.demonii.com:1337/announce",
                        "udp://tracker.tiny-vps.com:6969/announce",
                        "udp://tracker.port443.xyz:6969/announce",
                        "udp://tracker.moeking.me:6969/announce"
                    }
                },
                new TrackerCategory
                {
                    Name = "国内 Tracker",
                    Trackers = new List<string>
                    {
                        "http://tracker.bittorrent.am:80/announce",
                        "udp://tracker.bittorrent.am:80/announce",
                        "udp://tracker.torrent.eu.org:451/announce",
                        "http://tracker.torrenty.org:80/announce",
                        "http://retracker.mgts.by:80/announce",
                        "http://retracker.spb.ru:80/announce"
                    }
                }
            };
            _customTrackers = new List<string>();
            Save();
        }
    }
}