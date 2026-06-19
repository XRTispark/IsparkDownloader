using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using IsparkDownloader2.Core;
using IsparkDownloader2.Views;

namespace IsparkDownloader2
{
    public partial class App : Application
    {
        private UpdateChecker? _updateChecker;
        private ConfigManager? _configManager;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _configManager = new ConfigManager();

            // 启动时检查更新（如果启用）
            var autoCheck = _configManager.Get("AutoCheckUpdate", true);
            if (autoCheck)
            {
                await CheckForUpdateAsync(silent: true);
            }
        }

        /// <summary>
        /// 检查更新（可从菜单调用）
        /// </summary>
        public async Task CheckForUpdateAsync(bool silent = false)
        {
            _updateChecker ??= new UpdateChecker();

            // 检查是否跳过此版本
            var skippedVersion = _configManager?.Get("SkippedVersion", "") ?? "";

            _updateChecker.UpdateAvailable += (s, versionInfo) =>
            {
                // 如果用户跳过了这个版本，不再提示
                if (!string.IsNullOrEmpty(skippedVersion) && skippedVersion == versionInfo.Version)
                    return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var updateWindow = new UpdateWindow(versionInfo, _updateChecker!);
                    updateWindow.Owner = Application.Current.MainWindow;
                    updateWindow.ShowDialog();

                    if (updateWindow.ShouldSkipVersion)
                    {
                        // 记录跳过的版本
                        _configManager?.Set("SkippedVersion", versionInfo.Version);
                    }
                });
            };

            _updateChecker.CheckFailed += (s, message) =>
            {
                if (!silent)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(message, "检查更新失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                    });
                }
            };

            _updateChecker.UpdateFailed += (s, message) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
                });
            };

            await _updateChecker.CheckForUpdateAsync(silent);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _updateChecker?.Dispose();
            base.OnExit(e);
        }
    }
}
