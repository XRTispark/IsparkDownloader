using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using IsparkDownloader2.Core;

namespace IsparkDownloader2.Views
{
    public partial class BrowserSimWindow : Window
    {
        private readonly BrowserSimulator _browserSimulator;
        private readonly BrowserStorageImporter _importer;

        public BrowserSimWindow(BrowserSimulator browserSimulator)
        {
            InitializeComponent();
            _browserSimulator = browserSimulator;
            _importer = new BrowserStorageImporter();
            LoadSettings();
            LoadDetectedBrowsers();
        }

        private void LoadSettings()
        {
            var profile = _browserSimulator.Profile;
            EnableSimCheckBox.IsChecked = profile.EnableSimulation;
            UserAgentTextBox.Text = profile.UserAgent;
            AcceptLanguageTextBox.Text = profile.AcceptLanguage;
            RefererTextBox.Text = profile.Referer ?? "";
            DntCheckBox.IsChecked = profile.DoNotTrack;

            UpdateStatusTexts();
        }

        private void LoadDetectedBrowsers()
        {
            var browsers = _importer.DetectBrowsers();
            DetectedBrowsersListBox.ItemsSource = browsers;
        }

        private void UpdateStatusTexts()
        {
            var cookieCount = _browserSimulator.CookieContainer.Count;
            CookieStatusText.Text = $"当前 Cookie 数量: {cookieCount}";

            var lsCount = _browserSimulator.LocalStorage.Count;
            LocalStorageStatusText.Text = $"当前 LocalStorage 条目: {lsCount}";
        }

        private void AutoImportAll_Click(object sender, RoutedEventArgs e)
        {
            var domainFilter = DomainFilterTextBox.Text.Trim();
            if (string.IsNullOrEmpty(domainFilter))
                domainFilter = null;

            try
            {
                // 导入 Cookie
                var cookieResult = _importer.ImportAllCookies(_browserSimulator, domainFilter);

                // 导入 LocalStorage
                var lsResult = _importer.ImportAllLocalStorage(_browserSimulator, domainFilter);

                var totalCookies = cookieResult.CookieCount;
                var totalLS = lsResult.LocalStorageCount;
                var browsers = cookieResult.BrowsersProcessed;

                var msg = $"导入 {totalCookies} 个 Cookie，{totalLS} 条 LocalStorage\n处理了 {browsers} 个浏览器配置";

                if (totalCookies == 0 && totalLS == 0)
                {
                    msg += "\n\n可能原因：\n- 浏览器正在运行，数据库文件被锁定\n- 请关闭所有浏览器后重试\n- 或使用下方[导入文件]功能手动导入";
                }

                var allErrors = cookieResult.Errors.Concat(lsResult.Errors).ToList();
                if (allErrors.Count > 0)
                {
                    msg += $"\n\n详细错误:\n{string.Join("\n", allErrors.Take(5))}";
                }

                ImportStatusText.Text = msg;
                UpdateStatusTexts();

                MessageBox.Show(msg, "导入结果", MessageBoxButton.OK,
                    totalCookies > 0 || totalLS > 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportJson_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                Title = "选择 Cookie JSON 文件"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    var cookies = _importer.ImportFromJson(json);
                    foreach (var cookie in cookies)
                    {
                        var url = $"https://{cookie.Domain.TrimStart('.')}";
                        _browserSimulator.SetCookie(url, cookie.Name, cookie.Value, cookie.Expires);
                    }
                    UpdateStatusTexts();
                    MessageBox.Show($"成功导入 {cookies.Count} 个 Cookie", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportNetscape_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "选择 Netscape Cookie 文件"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var content = File.ReadAllText(dialog.FileName);
                    var cookies = _importer.ImportFromNetscape(content);
                    foreach (var cookie in cookies)
                    {
                        var url = $"https://{cookie.Domain.TrimStart('.')}";
                        _browserSimulator.SetCookie(url, cookie.Name, cookie.Value, cookie.Expires);
                    }
                    UpdateStatusTexts();
                    MessageBox.Show($"成功导入 {cookies.Count} 个 Cookie", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RandomizeUA_Click(object sender, RoutedEventArgs e)
        {
            UserAgentTextBox.Text = BrowserSimulator.GetRandomUserAgent();
        }

        private void AddCookie_Click(object sender, RoutedEventArgs e)
        {
            var domain = CookieDomainTextBox.Text.Trim();
            var name = CookieNameTextBox.Text.Trim();
            var value = CookieValueTextBox.Text.Trim();

            if (string.IsNullOrEmpty(domain) || string.IsNullOrEmpty(name))
            {
                MessageBox.Show("域名和 Cookie 名称不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var url = domain.StartsWith("http") ? domain : $"https://{domain}";
            _browserSimulator.SetCookie(url, name, value);
            UpdateStatusTexts();
            MessageBox.Show("Cookie 已添加", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearCookies_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要清除所有 Cookie 吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _browserSimulator.ClearCookies();
                UpdateStatusTexts();
            }
        }

        private void AddLocalStorage_Click(object sender, RoutedEventArgs e)
        {
            var key = LSKeyTextBox.Text.Trim();
            var value = LSValueTextBox.Text.Trim();

            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Key 不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _browserSimulator.SetLocalStorage(key, value);
            UpdateStatusTexts();
            MessageBox.Show("LocalStorage 已添加", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearLocalStorage_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要清除所有 LocalStorage 吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                foreach (var key in _browserSimulator.LocalStorage.Keys.ToList())
                {
                    _browserSimulator.RemoveLocalStorage(key);
                }
                UpdateStatusTexts();
            }
        }

        private void AddCustomHeader_Click(object sender, RoutedEventArgs e)
        {
            var key = CustomHeaderKeyTextBox.Text.Trim();
            var value = CustomHeaderValueTextBox.Text.Trim();

            if (string.IsNullOrEmpty(key))
            {
                MessageBox.Show("Header 名称不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _browserSimulator.Profile.CustomHeaders[key] = value;
            MessageBox.Show("自定义请求头已添加", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RandomizeFingerprint_Click(object sender, RoutedEventArgs e)
        {
            _browserSimulator.RandomizeFingerprint();
            UserAgentTextBox.Text = _browserSimulator.Profile.UserAgent;
            MessageBox.Show("浏览器指纹已随机化", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var profile = _browserSimulator.Profile;
            profile.EnableSimulation = EnableSimCheckBox.IsChecked ?? true;
            profile.UserAgent = UserAgentTextBox.Text;
            profile.AcceptLanguage = AcceptLanguageTextBox.Text;
            profile.Referer = string.IsNullOrEmpty(RefererTextBox.Text) ? null : RefererTextBox.Text;
            profile.DoNotTrack = DntCheckBox.IsChecked ?? false;

            _browserSimulator.SaveData();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
