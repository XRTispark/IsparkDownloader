using System.Collections.ObjectModel;
using System.IO;
using IsparkDownloader2.Models;
using Newtonsoft.Json;

namespace IsparkDownloader2.Core.CloudDrive
{
    /// <summary>
    /// 网盘账号配置管理器
    /// </summary>
    public class CloudDriveConfigManager
    {
        private readonly string _configPath;
        private ObservableCollection<CloudDriveAccount> _accounts = new();

        public ObservableCollection<CloudDriveAccount> Accounts => _accounts;

        public CloudDriveConfigManager(string configPath)
        {
            _configPath = configPath;
            LoadAccounts();
        }

        public void AddAccount(CloudDriveAccount account)
        {
            _accounts.Add(account);
            SaveAccounts();
        }

        public void RemoveAccount(string accountId)
        {
            var account = _accounts.FirstOrDefault(a => a.Id == accountId);
            if (account != null)
            {
                _accounts.Remove(account);
                SaveAccounts();
            }
        }

        public void UpdateAccount(CloudDriveAccount account)
        {
            var existing = _accounts.FirstOrDefault(a => a.Id == account.Id);
            if (existing != null)
            {
                var index = _accounts.IndexOf(existing);
                _accounts[index] = account;
                SaveAccounts();
            }
        }

        public CloudDriveAccount? GetAccount(CloudDriveType driveType)
        {
            return _accounts.FirstOrDefault(a => a.DriveType == driveType && a.IsLoggedIn);
        }

        public CloudDriveAccount? GetAccountById(string accountId)
        {
            return _accounts.FirstOrDefault(a => a.Id == accountId);
        }

        public bool HasAccount(CloudDriveType driveType)
        {
            return _accounts.Any(a => a.DriveType == driveType && a.IsLoggedIn);
        }

        private void SaveAccounts()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
                Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }

        private void LoadAccounts()
        {
            try
            {
                if (!File.Exists(_configPath)) return;
                var json = File.ReadAllText(_configPath);
                var accounts = JsonConvert.DeserializeObject<List<CloudDriveAccount>>(json);
                if (accounts != null)
                {
                    foreach (var account in accounts)
                        _accounts.Add(account);
                }
            }
            catch { }
        }
    }
}
