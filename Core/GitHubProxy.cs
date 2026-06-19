namespace IsparkDownloader2.Core
{
    /// <summary>
    /// GitHub 加速代理工具
    /// 自动将 GitHub 相关链接转换为加速镜像链接
    /// </summary>
    public static class GitHubProxy
    {
        // 加速代理列表（按优先级排序，自动回退）
        private static readonly string[] Proxies =
        {
            "https://gh-proxy.com",
            "https://mirror.ghproxy.com",
            "https://ghproxy.net",
            "https://gh-proxy.org"
        };

        private static readonly string[] GitHubDomains =
        {
            "github.com",
            "raw.githubusercontent.com",
            "objects.githubusercontent.com",
            "archives.githubusercontent.com",
            "github-releases.githubusercontent.com",
            "codeload.github.com"
        };

        /// <summary>
        /// 判断 URL 是否为 GitHub 相关链接
        /// </summary>
        public static bool IsGitHubUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            var lower = url.ToLowerInvariant();
            foreach (var domain in GitHubDomains)
            {
                if (lower.Contains(domain)) return true;
            }
            return false;
        }

        /// <summary>
        /// 将 GitHub URL 转换为加速链接
        /// </summary>
        public static string GetProxiedUrl(string url)
        {
            if (string.IsNullOrEmpty(url) || !IsGitHubUrl(url)) return url;
            return $"{Proxies[0]}/{url}";
        }

        /// <summary>
        /// 获取所有可用的加速 URL 列表（用于回退）
        /// </summary>
        public static List<string> GetAllProxiedUrls(string url)
        {
            var urls = new List<string>();
            if (string.IsNullOrEmpty(url) || !IsGitHubUrl(url))
            {
                urls.Add(url);
                return urls;
            }
            foreach (var proxy in Proxies)
            {
                urls.Add($"{proxy}/{url}");
            }
            // 最后加上原始 URL 作为最终回退
            urls.Add(url);
            return urls;
        }
    }
}
