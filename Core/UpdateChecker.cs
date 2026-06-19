using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using IsparkDownloader2.Views;

namespace IsparkDownloader2.Core
{
    /// <summary>
    /// 版本信息
    /// </summary>
    public class VersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public long FileSize { get; set; }
        public bool IsNewer { get; set; }
    }

    /// <summary>
    /// 自动更新检查器
    /// 通过 GitHub Releases API 检查最新版本
    /// </summary>
    public class UpdateChecker
    {
        private readonly HttpClient _httpClient;
        private readonly string _owner = "XRTispark";
        private readonly string _repo = "IsparkDownloader";
        private readonly string _currentVersion;
        private readonly string _updateScriptPath;

        public event EventHandler<VersionInfo>? UpdateAvailable;
        public event EventHandler<string>? CheckFailed;
        public event EventHandler? UpdateStarted;
        public event EventHandler? UpdateCompleted;
        public event EventHandler<string>? UpdateFailed;

        public UpdateChecker()
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "IsparkDownloader2-UpdateChecker");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            // 获取当前版本号
            _currentVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0.0";

            // 更新脚本路径（放在临时目录）
            _updateScriptPath = Path.Combine(Path.GetTempPath(), "IsparkDownloader_Update.bat");
        }

        /// <summary>
        /// 异步检查更新
        /// </summary>
        public async Task CheckForUpdateAsync(bool silent = false)
        {
            try
            {
                // GitHub API 也通过加速代理
                var apiUrl = GitHubProxy.GetProxiedUrl($"https://api.github.com/repos/{_owner}/{_repo}/releases/latest");
                var response = await _httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    if (!silent)
                        CheckFailed?.Invoke(this, $"检查更新失败: HTTP {(int)response.StatusCode}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                var release = JsonNode.Parse(json);

                if (release == null)
                {
                    if (!silent)
                        CheckFailed?.Invoke(this, "解析版本信息失败");
                    return;
                }

                var tagName = release["tag_name"]?.GetValue<string>() ?? "";
                var latestVersion = tagName.TrimStart('v', 'V');
                var publishedAt = release["published_at"]?.GetValue<string>() ?? "";
                var releaseNotes = release["body"]?.GetValue<string>() ?? "";

                // 查找 Windows x64 资产
                string downloadUrl = "";
                long fileSize = 0;
                var assets = release["assets"]?.AsArray();
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset?["name"]?.GetValue<string>() ?? "";
                        if (name.Contains("win-x64") && name.EndsWith(".zip"))
                        {
                            downloadUrl = asset?["browser_download_url"]?.GetValue<string>() ?? "";
                            fileSize = asset?["size"]?.GetValue<long>() ?? 0;
                            break;
                        }
                    }
                }

                // 如果没有找到特定资产，使用发布页面的 zipball
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = release["zipball_url"]?.GetValue<string>() ?? "";
                }

                var isNewer = IsVersionNewer(latestVersion, _currentVersion);

                var versionInfo = new VersionInfo
                {
                    Version = latestVersion,
                    DownloadUrl = downloadUrl,
                    ReleaseNotes = releaseNotes,
                    PublishedAt = DateTime.TryParse(publishedAt, out var dt) ? dt : DateTime.MinValue,
                    FileSize = fileSize,
                    IsNewer = isNewer
                };

                if (isNewer)
                {
                    UpdateAvailable?.Invoke(this, versionInfo);
                }
                else if (!silent)
                {
                    // 非静默模式下提示已是最新
                    MessageBox.Show($"当前版本 {_currentVersion} 已是最新版本。", "检查更新",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (TaskCanceledException)
            {
                if (!silent)
                    CheckFailed?.Invoke(this, "检查更新超时，请检查网络连接");
            }
            catch (Exception ex)
            {
                if (!silent)
                    CheckFailed?.Invoke(this, $"检查更新时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行更新：下载新版本并覆盖安装
        /// </summary>
        public async Task PerformUpdateAsync(VersionInfo versionInfo, IProgress<double>? progress = null)
        {
            try
            {
                UpdateStarted?.Invoke(this, EventArgs.Empty);

                var tempDir = Path.Combine(Path.GetTempPath(), "IsparkDownloader_Update");
                var zipPath = Path.Combine(tempDir, "update.zip");
                var extractPath = Path.Combine(tempDir, "extracted");

                // 清理旧文件
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);
                Directory.CreateDirectory(extractPath);

                // 下载更新包（GitHub 链接加速）
                progress?.Report(0.05);
                var proxiedDownloadUrl = GitHubProxy.GetProxiedUrl(versionInfo.DownloadUrl);
                await DownloadFileAsync(proxiedDownloadUrl, zipPath, progress);
                progress?.Report(0.6);

                // 解压更新包
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath, true);
                progress?.Report(0.75);

                // 查找解压后的 EXE 文件
                var newExePath = FindExeInDirectory(extractPath);
                if (string.IsNullOrEmpty(newExePath))
                {
                    throw new Exception("更新包中未找到可执行文件");
                }
                progress?.Report(0.8);

                // 获取当前程序路径
                var currentExePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? Assembly.GetExecutingAssembly().Location;
                var currentDir = Path.GetDirectoryName(currentExePath)!;
                var currentExeName = Path.GetFileName(currentExePath);

                // 创建更新批处理脚本
                CreateUpdateScript(currentExePath, newExePath, currentDir, currentExeName);
                progress?.Report(0.9);

                // 启动更新脚本并退出当前程序
                var psi = new ProcessStartInfo
                {
                    FileName = _updateScriptPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = currentDir
                };
                Process.Start(psi);
                progress?.Report(1.0);

                UpdateCompleted?.Invoke(this, EventArgs.Empty);

                // 退出当前应用程序
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                UpdateFailed?.Invoke(this, $"更新失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 下载文件并报告进度
        /// </summary>
        private async Task DownloadFileAsync(string url, string savePath, IProgress<double>? progress)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(savePath, FileMode.Create, FileAccess.Write);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                downloadedBytes += read;

                if (totalBytes > 0 && progress != null)
                {
                    var downloadProgress = downloadedBytes / (double)totalBytes;
                    // 下载占总更新的 55% (5% ~ 60%)
                    progress.Report(0.05 + downloadProgress * 0.55);
                }
            }
        }

        /// <summary>
        /// 在目录中查找 EXE 文件
        /// </summary>
        private string? FindExeInDirectory(string dir)
        {
            // 优先查找与当前程序同名的 EXE
            var currentExeName = Path.GetFileNameWithoutExtension(
                Process.GetCurrentProcess().MainModule?.FileName ?? "IsparkDownloader2.exe");

            var exeFiles = Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories);
            
            // 优先匹配同名
            var match = exeFiles.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(currentExeName, StringComparison.OrdinalIgnoreCase));
            
            return match ?? exeFiles.FirstOrDefault();
        }

        /// <summary>
        /// 创建更新批处理脚本
        /// </summary>
        private void CreateUpdateScript(string currentExePath, string newExePath, string targetDir, string exeName)
        {
            var script = $@"@echo off
chcp 65001 >nul
title IsparkDownloader 更新程序
echo ==========================================
echo    IsparkDownloader 正在更新...
echo ==========================================
echo.

:: 等待原程序退出
timeout /t 2 /nobreak >nul

:: 备份当前版本
echo [1/4] 备份当前版本...
if exist ""{targetDir}\{exeName}.backup"" del /F /Q ""{targetDir}\{exeName}.backup"" >nul 2>&1
move /Y ""{currentExePath}"" ""{targetDir}\{exeName}.backup"" >nul 2>&1

:: 复制新版本
echo [2/4] 安装新版本...
copy /Y ""{newExePath}"" ""{targetDir}\{exeName}"" >nul 2>&1
if errorlevel 1 (
    echo 复制失败，正在恢复备份...
    move /Y ""{targetDir}\{exeName}.backup"" ""{currentExePath}"" >nul 2>&1
    echo 更新失败！请手动下载更新。
    pause
    exit /b 1
)

:: 复制其他依赖文件
echo [3/4] 复制依赖文件...
for %f in (""{Path.GetDirectoryName(newExePath)}\*.*"") do (
    if /I not ""%~nxf""==""{exeName}"" (
        copy /Y ""%f"" ""{targetDir}\"" >nul 2>&1
    )
)

:: 清理备份和临时文件
echo [4/4] 清理临时文件...
del /F /Q ""{targetDir}\{exeName}.backup"" >nul 2>&1
rmdir /S /Q ""{Path.GetDirectoryName(newExePath)}"" >nul 2>&1
rmdir /S /Q ""{Path.GetTempPath()}IsparkDownloader_Update"" >nul 2>&1
del /F /Q ""%~f0"" >nul 2>&1

echo.
echo ==========================================
echo    更新完成！正在启动新版本...
echo ==========================================
timeout /t 1 /nobreak >nul

:: 启动新版本
start "" ""{targetDir}\{exeName}""

exit
";

            File.WriteAllText(_updateScriptPath, script, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// 比较版本号
        /// </summary>
        private bool IsVersionNewer(string latest, string current)
        {
            try
            {
                var latestParts = latest.Split('.');
                var currentParts = current.Split('.');

                var maxLength = Math.Max(latestParts.Length, currentParts.Length);
                for (int i = 0; i < maxLength; i++)
                {
                    var latestPart = i < latestParts.Length && int.TryParse(latestParts[i], out var lp) ? lp : 0;
                    var currentPart = i < currentParts.Length && int.TryParse(currentParts[i], out var cp) ? cp : 0;

                    if (latestPart > currentPart) return true;
                    if (latestPart < currentPart) return false;
                }
                return false; // 版本相同
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
