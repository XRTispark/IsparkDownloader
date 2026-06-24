using System.IO;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            _configManager = new ConfigManager();

            // 异步检查更新，不阻塞 UI 显示
            var autoCheck = _configManager.Get("AutoCheckUpdate", true);
            if (autoCheck)
            {
                _ = CheckForUpdateAsync(silent: true);
            }
        }

        /// <summary>
        /// 检查更新（可从菜单调用）
        /// </summary>
        public async Task CheckForUpdateAsync(bool silent = false)
        {
            try
            {
                _updateChecker ??= new UpdateChecker();

                var skippedVersion = _configManager?.Get("SkippedVersion", "") ?? "";

                _updateChecker.UpdateAvailable += (s, versionInfo) =>
                {
                    if (!string.IsNullOrEmpty(skippedVersion) && skippedVersion == versionInfo.Version)
                        return;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var updateWindow = new UpdateWindow(versionInfo, _updateChecker!);
                        updateWindow.Owner = Application.Current.MainWindow;
                        updateWindow.ShowDialog();

                        if (updateWindow.ShouldSkipVersion)
                        {
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
            catch
            {
                // 静默模式不报错
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _updateChecker?.Dispose();
            base.OnExit(e);
        }
    }
}
