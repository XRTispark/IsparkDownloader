using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using IsparkDownloader2.Core;

namespace IsparkDownloader2.Views
{
    public partial class UpdateWindow : Window
    {
        private readonly VersionInfo _versionInfo;
        private readonly UpdateChecker _updateChecker;

        public bool ShouldSkipVersion { get; private set; }
        public bool ShouldUpdate { get; private set; }

        public UpdateWindow(VersionInfo versionInfo, UpdateChecker updateChecker)
        {
            _versionInfo = versionInfo;
            _updateChecker = updateChecker;
            InitializeComponent();
            LoadVersionInfo();
        }

        private void LoadVersionInfo()
        {
            // 当前版本
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";
            CurrentVersionText.Text = $"v{currentVersion}";

            // 新版本
            NewVersionText.Text = $"v{_versionInfo.Version}";
            VersionText.Text = $"最新版本: v{_versionInfo.Version}";

            // 发布日期
            ReleaseDateText.Text = _versionInfo.PublishedAt == DateTime.MinValue
                ? "未知"
                : _versionInfo.PublishedAt.ToString("yyyy-MM-dd HH:mm");

            // 更新内容
            var notes = string.IsNullOrWhiteSpace(_versionInfo.ReleaseNotes)
                ? "暂无更新说明"
                : _versionInfo.ReleaseNotes;
            ReleaseNotesText.Text = notes;
        }

        /// <summary>立即更新</summary>
        private async void UpdateNow_Click(object sender, RoutedEventArgs e)
        {
            UpdateNowButton.IsEnabled = false;
            LaterButton.IsEnabled = false;
            SkipButton.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;

            var progress = new Progress<double>(value =>
            {
                UpdateProgressBar.Value = value;
                ProgressText.Text = $"正在更新... {(value * 100):F0}%";
            });

            try
            {
                await _updateChecker.PerformUpdateAsync(_versionInfo, progress);
                ShouldUpdate = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"更新失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateNowButton.IsEnabled = true;
                LaterButton.IsEnabled = true;
                SkipButton.IsEnabled = true;
                ProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>稍后提醒</summary>
        private void Later_Click(object sender, RoutedEventArgs e)
        {
            ShouldSkipVersion = false;
            ShouldUpdate = false;
            DialogResult = false;
            Close();
        }

        /// <summary>跳过此版本</summary>
        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            ShouldSkipVersion = true;
            ShouldUpdate = false;
            DialogResult = false;
            Close();
        }
    }
}
