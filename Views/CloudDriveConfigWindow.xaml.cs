using System.Diagnostics;
using System.Windows;
using IsparkDownloader2.Core.CloudDrive;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Views
{
    public partial class CloudDriveConfigWindow : Window
    {
        private readonly CloudDriveEngine _engine;

        public CloudDriveConfigWindow(CloudDriveEngine engine)
        {
            _engine = engine;
            InitializeComponent();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            // 百度网盘状态
            var baiduAccount = _engine.ConfigManager.GetAccount(CloudDriveType.BaiduPan);
            if (baiduAccount != null && baiduAccount.IsLoggedIn)
            {
                var spaceText = baiduAccount.TotalSpace > 0
                    ? $"空间: {baiduAccount.UsedSpace / 1024.0 / 1024.0 / 1024.0:F2} / {baiduAccount.TotalSpace / 1024.0 / 1024.0 / 1024.0:F2} GB"
                    : "";
                BaiduStatusText.Text = $"已登录: {baiduAccount.UserName}\n{spaceText}";
                BaiduStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                BaiduStatusText.Text = "未登录\n请在百度网盘开放平台 (https://pan.baidu.com/union) 申请 AppKey 和 SecretKey";
                BaiduStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }

            // 阿里云盘状态
            var aliyunAccount = _engine.ConfigManager.GetAccount(CloudDriveType.AliyunDrive);
            if (aliyunAccount != null && aliyunAccount.IsLoggedIn)
            {
                AliyunStatusText.Text = $"已登录: {aliyunAccount.UserName}";
                AliyunStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                AliyunStatusText.Text = "未登录\n请在阿里云盘开放平台 (https://www.alipan.com/developer) 申请 Client ID 和 Secret";
                AliyunStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }

            // 夸克网盘状态
            var quarkAccount = _engine.ConfigManager.GetAccount(CloudDriveType.QuarkDrive);
            if (quarkAccount != null && quarkAccount.IsLoggedIn)
            {
                var spaceText = quarkAccount.TotalSpace > 0
                    ? $"空间: {quarkAccount.UsedSpace / 1024.0 / 1024.0 / 1024.0:F2} / {quarkAccount.TotalSpace / 1024.0 / 1024.0 / 1024.0:F2} GB"
                    : "";
                QuarkStatusText.Text = $"已登录: {quarkAccount.UserName}\n{spaceText}";
                QuarkStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                QuarkStatusText.Text = "未登录\n获取方式: 登录 pan.quark.cn 后，在浏览器开发者工具(F12)的 Application/Cookies 中查找 ctoken 值";
                QuarkStatusText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        // ===== 百度网盘 =====
        private void GetBaiduAuthUrl_Click(object sender, RoutedEventArgs e)
        {
            var clientId = BaiduClientId.Text.Trim();
            if (string.IsNullOrEmpty(clientId))
            {
                MessageBox.Show("请先输入 AppKey", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var redirectUri = "oob"; // 百度网盘支持 oob 方式
            var authUrl = _engine.GetAuthUrl(CloudDriveType.BaiduPan, clientId, redirectUri);

            Clipboard.SetText(authUrl);
            MessageBox.Show($"授权链接已复制到剪贴板，请在浏览器中打开并授权。\n\n{authUrl}", "授权链接", MessageBoxButton.OK, MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }

        private async void BaiduLogin_Click(object sender, RoutedEventArgs e)
        {
            var clientId = BaiduClientId.Text.Trim();
            var clientSecret = BaiduClientSecret.Text.Trim();
            var authCode = BaiduAuthCode.Text.Trim();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(authCode))
            {
                MessageBox.Show("请填写完整的 AppKey、SecretKey 和授权码", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = await _engine.LoginAsync(CloudDriveType.BaiduPan, clientId, clientSecret, authCode, "oob");
            if (result)
            {
                MessageBox.Show("百度网盘登录成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus();
            }
            else
            {
                MessageBox.Show("登录失败，请检查 AppKey、SecretKey 和授权码是否正确", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BaiduLogout_Click(object sender, RoutedEventArgs e)
        {
            _engine.Logout(CloudDriveType.BaiduPan);
            UpdateStatus();
            MessageBox.Show("已登出百度网盘", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ===== 阿里云盘 =====
        private void GetAliyunAuthUrl_Click(object sender, RoutedEventArgs e)
        {
            var clientId = AliyunClientId.Text.Trim();
            if (string.IsNullOrEmpty(clientId))
            {
                MessageBox.Show("请先输入 Client ID", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var redirectUri = "https://www.alipan.com/oauth/callback";
            var authUrl = _engine.GetAuthUrl(CloudDriveType.AliyunDrive, clientId, redirectUri);

            Clipboard.SetText(authUrl);
            MessageBox.Show($"授权链接已复制到剪贴板，请在浏览器中打开并授权。\n\n{authUrl}", "授权链接", MessageBoxButton.OK, MessageBoxImage.Information);
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
        }

        private async void AliyunLogin_Click(object sender, RoutedEventArgs e)
        {
            var clientId = AliyunClientId.Text.Trim();
            var clientSecret = AliyunClientSecret.Text.Trim();
            var authCode = AliyunAuthCode.Text.Trim();

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(authCode))
            {
                MessageBox.Show("请填写完整的 Client ID、Secret 和授权码", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = await _engine.LoginAsync(CloudDriveType.AliyunDrive, clientId, clientSecret, authCode, "https://www.alipan.com/oauth/callback");
            if (result)
            {
                MessageBox.Show("阿里云盘登录成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus();
            }
            else
            {
                MessageBox.Show("登录失败，请检查 Client ID、Secret 和授权码是否正确", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AliyunLogout_Click(object sender, RoutedEventArgs e)
        {
            _engine.Logout(CloudDriveType.AliyunDrive);
            UpdateStatus();
            MessageBox.Show("已登出阿里云盘", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ===== 夸克网盘 =====
        private async void QuarkLogin_Click(object sender, RoutedEventArgs e)
        {
            var token = QuarkToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show("请输入 ctoken", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = await _engine.LoginWithTokenAsync(CloudDriveType.QuarkDrive, token);
            if (result)
            {
                MessageBox.Show("夸克网盘登录成功！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStatus();
            }
            else
            {
                MessageBox.Show("登录失败，请检查 ctoken 是否正确", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void QuarkLogout_Click(object sender, RoutedEventArgs e)
        {
            _engine.Logout(CloudDriveType.QuarkDrive);
            UpdateStatus();
            MessageBox.Show("已登出夸克网盘", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
