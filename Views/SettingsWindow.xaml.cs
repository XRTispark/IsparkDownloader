using System;
using System.IO;
using System.Windows;
using IsparkDownloader2.Core;
using Microsoft.Win32;

namespace IsparkDownloader2.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly ConfigManager _configManager;

        public SettingsWindow(ConfigManager configManager)
        {
            InitializeComponent();
            _configManager = configManager;
            LoadSettings();
        }

        private void LoadSettings()
        {
            var config = _configManager.Config;
            SavePathTextBox.Text = config.DefaultSavePath;
            ThreadCountTextBox.Text = config.DefaultThreadCount.ToString();
            MaxConcurrentTextBox.Text = config.MaxConcurrentDownloads.ToString();
            SpeedLimitTextBox.Text = (config.DefaultSpeedLimit / 1024).ToString();
            AutoStartCheckBox.IsChecked = config.AutoStartDownload;
            NotificationCheckBox.IsChecked = config.ShowNotificationOnComplete;
            AutoRemoveCheckBox.IsChecked = config.AutoRemoveCompleted;
            AutoCheckUpdateCheckBox.IsChecked = _configManager.Get("AutoCheckUpdate", true);
            TimeoutTextBox.Text = config.ConnectionTimeout.ToString();
            RetryCountTextBox.Text = config.RetryCount.ToString();
            UserAgentTextBox.Text = config.UserAgent;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = _configManager.Config;
                config.DefaultSavePath = SavePathTextBox.Text;
                config.DefaultThreadCount = int.Parse(ThreadCountTextBox.Text);
                config.MaxConcurrentDownloads = int.Parse(MaxConcurrentTextBox.Text);
                config.DefaultSpeedLimit = long.Parse(SpeedLimitTextBox.Text) * 1024;
                config.AutoStartDownload = AutoStartCheckBox.IsChecked ?? true;
                config.ShowNotificationOnComplete = NotificationCheckBox.IsChecked ?? true;
                config.AutoRemoveCompleted = AutoRemoveCheckBox.IsChecked ?? false;
                config.ConnectionTimeout = int.Parse(TimeoutTextBox.Text);
                config.RetryCount = int.Parse(RetryCountTextBox.Text);
                config.UserAgent = UserAgentTextBox.Text;
                _configManager.Set("AutoCheckUpdate", AutoCheckUpdateCheckBox.IsChecked ?? true);

                _configManager.Save();
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存设置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "选择默认保存目录",
                FolderName = SavePathTextBox.Text
            };
            if (dialog.ShowDialog() == true)
            {
                SavePathTextBox.Text = dialog.FolderName;
            }
        }
    }
}
