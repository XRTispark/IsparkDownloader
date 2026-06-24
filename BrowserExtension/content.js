// content.js - 注入页面，捕获下载链接

(function() {
    'use strict';

    // 避免重复注入
    if (window.__isparkDownloaderInjected) return;
    window.__isparkDownloaderInjected = true;

    // 存储检测到的下载链接
    const detectedLinks = new Map();

    // 常见的下载文件扩展名
    const DOWNLOAD_EXTENSIONS = [
        '.exe', '.msi', '.zip', '.rar', '.7z', '.tar', '.gz', '.bz2',
        '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
        '.mp3', '.mp4', '.avi', '.mkv', '.mov', '.wmv', '.flv',
        '.jpg', '.jpeg', '.png', '.gif', '.bmp', '.webp', '.svg',
        '.iso', '.dmg', '.apk', '.ipa', '.deb', '.rpm',
        '.torrent', '.magnet'
    ];

    // 检测 URL 是否是下载链接
    function isDownloadUrl(url) {
        try {
            const urlObj = new URL(url);
            const pathname = urlObj.pathname.toLowerCase();
            const search = urlObj.search.toLowerCase();

            // 检查扩展名
            for (const ext of DOWNLOAD_EXTENSIONS) {
                if (pathname.endsWith(ext)) return true;
            }

            // 检查常见的下载参数
            const downloadParams = ['download', 'attachment', 'file', 'blob'];
            for (const param of downloadParams) {
                if (search.includes(param)) return true;
            }

            // 检查 GitHub Release 链接
            if (urlObj.hostname.includes('github.com') && pathname.includes('/releases/download/')) {
                return true;
            }

            // 检查网盘分享链接
            if (urlObj.hostname.includes('pan.baidu.com') ||
                urlObj.hostname.includes('aliyundrive.com') ||
                urlObj.hostname.includes('quark.cn')) {
                return pathname.includes('/s/') || pathname.includes('/share/');
            }

            return false;
        } catch {
            return false;
        }
    }

    // 获取文件名
    function getFileName(url) {
        try {
            const urlObj = new URL(url);
            const pathname = decodeURIComponent(urlObj.pathname);
            const fileName = pathname.split('/').pop();
            return fileName || '未知文件';
        } catch {
            return '未知文件';
        }
    }

    // 获取文件大小（从页面元素中查找）
    function getFileSize(element) {
        // 尝试从附近的文本中获取文件大小
        const parent = element.closest('tr, li, div, article');
        if (parent) {
            const text = parent.textContent;
            const sizeMatch = text.match(/(\d+\.?\d*\s*(B|KB|MB|GB|TB))/i);
            if (sizeMatch) return sizeMatch[1];
        }
        return '';
    }

    // 添加链接到检测列表
    function addLink(url, source, element) {
        if (!url || url.startsWith('javascript:') || url.startsWith('#')) return;
        if (detectedLinks.has(url)) return;

        const fileName = getFileName(url);
        const fileSize = getFileSize(element);

        detectedLinks.set(url, {
            url: url,
            fileName: fileName,
            fileSize: fileSize,
            source: source,
            timestamp: Date.now()
        });

        // 通知 background script
        chrome.runtime.sendMessage({
            type: 'LINK_DETECTED',
            data: {
                url: url,
                fileName: fileName,
                fileSize: fileSize,
                source: source,
                pageUrl: window.location.href,
                pageTitle: document.title
            }
        }).catch(() => {});
    }

    // 扫描页面中的所有链接
    function scanLinks() {
        // 扫描 <a> 标签
        const links = document.querySelectorAll('a[href]');
        links.forEach(link => {
            const href = link.href;
            if (isDownloadUrl(href)) {
                addLink(href, 'link', link);
            }
        });

        // 扫描 <button> 和带有 data-download 属性的元素
        const downloadElements = document.querySelectorAll('[data-download], [download]');
        downloadElements.forEach(el => {
            const url = el.getAttribute('data-download') || el.getAttribute('href');
            if (url) addLink(url, 'button', el);
        });

        // 扫描视频和音频元素
        const mediaElements = document.querySelectorAll('video[src], audio[src], source[src]');
        mediaElements.forEach(el => {
            const src = el.src || el.getAttribute('src');
            if (src) addLink(src, 'media', el);
        });

        // 扫描 iframe
        const iframes = document.querySelectorAll('iframe[src]');
        iframes.forEach(iframe => {
            const src = iframe.src;
            if (isDownloadUrl(src)) {
                addLink(src, 'iframe', iframe);
            }
        });
    }

    // 拦截 XMLHttpRequest
    const originalXHR = window.XMLHttpRequest;
    window.XMLHttpRequest = function() {
        const xhr = new originalXHR();
        const originalOpen = xhr.open;
        xhr.open = function(method, url) {
            if (isDownloadUrl(url)) {
                addLink(url, 'xhr', null);
            }
            return originalOpen.apply(xhr, arguments);
        };
        return xhr;
    };

    // 拦截 fetch
    const originalFetch = window.fetch;
    window.fetch = function(url, options) {
        const urlStr = url.toString();
        if (isDownloadUrl(urlStr)) {
            addLink(urlStr, 'fetch', null);
        }
        return originalFetch.apply(window, arguments);
    };

    // 监听动态添加的元素
    const observer = new MutationObserver((mutations) => {
        mutations.forEach(mutation => {
            mutation.addedNodes.forEach(node => {
                if (node.nodeType === Node.ELEMENT_NODE) {
                    const element = node;

                    // 检查新添加的元素本身
                    if (element.tagName === 'A' && element.href) {
                        if (isDownloadUrl(element.href)) {
                            addLink(element.href, 'dynamic', element);
                        }
                    }

                    // 检查新添加的元素内部的链接
                    const links = element.querySelectorAll ? element.querySelectorAll('a[href]') : [];
                    links.forEach(link => {
                        if (isDownloadUrl(link.href)) {
                            addLink(link.href, 'dynamic', link);
                        }
                    });
                }
            });
        });
    });

    observer.observe(document.body, {
        childList: true,
        subtree: true
    });

    // 初始扫描
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', scanLinks);
    } else {
        scanLinks();
    }

    // 定期重新扫描（处理懒加载内容）
    setInterval(scanLinks, 3000);

    // ========== 鼠标悬停下载浮层 ==========

    let hoverPopup = null;
    let currentHoverLink = null;
    let hoverTimeout = null;
    let hoverEnabled = true;

    // 加载设置
    chrome.storage.local.get(['hoverEnabled'], (result) => {
        hoverEnabled = result.hoverEnabled !== false;
    });

    // 监听设置变化
    chrome.storage.onChanged.addListener((changes, area) => {
        if (area === 'local' && changes.hoverEnabled) {
            hoverEnabled = changes.hoverEnabled.newValue !== false;
        }
    });

    // 创建悬停浮层
    function createHoverPopup() {
        if (hoverPopup) return;

        const popup = document.createElement('div');
        popup.id = '__ispark_hover_popup';
        popup.innerHTML = `
            <div class="ispark-hover-inner">
                <div class="ispark-hover-header">
                    <span class="ispark-hover-icon">&#11015;</span>
                    <span class="ispark-hover-title">IsparkDownloader2</span>
                </div>
                <div class="ispark-hover-filename"></div>
                <div class="ispark-hover-url"></div>
                <div class="ispark-hover-actions">
                    <button class="ispark-btn ispark-btn-primary" data-action="download">
                        <span>&#11015;</span> 立即下载
                    </button>
                    <button class="ispark-btn ispark-btn-secondary" data-action="copy">
                        <span>&#128203;</span> 复制链接
                    </button>
                </div>
            </div>
        `;
        document.body.appendChild(popup);
        hoverPopup = popup;

        // 绑定按钮事件
        popup.querySelector('[data-action="download"]').addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            if (currentHoverLink) {
                sendToDownloader(currentHoverLink);
                hideHoverPopup();
            }
        });

        popup.querySelector('[data-action="copy"]').addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            if (currentHoverLink) {
                copyToClipboard(currentHoverLink.url);
                showHoverToast('链接已复制');
            }
        });

        // 鼠标进入浮层时不隐藏
        popup.addEventListener('mouseenter', () => {
            clearTimeout(hoverTimeout);
        });

        popup.addEventListener('mouseleave', () => {
            hideHoverPopup();
        });
    }

    // 显示悬停浮层
    function showHoverPopup(linkData, targetElement) {
        if (!hoverEnabled) return;
        if (!hoverPopup) createHoverPopup();

        clearTimeout(hoverTimeout);
        currentHoverLink = linkData;

        const filenameEl = hoverPopup.querySelector('.ispark-hover-filename');
        const urlEl = hoverPopup.querySelector('.ispark-hover-url');

        filenameEl.textContent = linkData.fileName;
        urlEl.textContent = linkData.url;

        // 计算位置
        const rect = targetElement.getBoundingClientRect();
        const popupRect = hoverPopup.getBoundingClientRect();

        let left = rect.left;
        let top = rect.bottom + 8;

        // 防止超出视口右边界
        if (left + 320 > window.innerWidth) {
            left = window.innerWidth - 330;
        }
        // 防止超出视口下边界
        if (top + 140 > window.innerHeight) {
            top = rect.top - 150;
        }

        hoverPopup.style.left = `${left + window.scrollX}px`;
        hoverPopup.style.top = `${top + window.scrollY}px`;
        hoverPopup.style.opacity = '1';
        hoverPopup.style.transform = 'translateY(0)';
        hoverPopup.style.pointerEvents = 'auto';
    }

    // 隐藏悬停浮层
    function hideHoverPopup() {
        if (!hoverPopup) return;
        hoverTimeout = setTimeout(() => {
            hoverPopup.style.opacity = '0';
            hoverPopup.style.transform = 'translateY(-8px)';
            hoverPopup.style.pointerEvents = 'none';
            currentHoverLink = null;
        }, 200);
    }

    // 显示临时提示
    function showHoverToast(message) {
        const toast = document.createElement('div');
        toast.className = '__ispark_hover_toast';
        toast.textContent = message;
        document.body.appendChild(toast);
        setTimeout(() => {
            toast.style.opacity = '0';
            setTimeout(() => toast.remove(), 300);
        }, 1500);
    }

    // 发送下载任务
    function sendToDownloader(link) {
        chrome.runtime.sendMessage({
            type: 'SEND_TO_DOWNLOADER',
            data: {
                url: link.url,
                fileName: link.fileName,
                fileSize: link.fileSize,
                pageUrl: window.location.href,
                pageTitle: document.title
            }
        }).catch(() => {
            copyToClipboard(link.url);
            showHoverToast('已复制到剪贴板');
        });
    }

    // 复制到剪贴板
    function copyToClipboard(text) {
        navigator.clipboard.writeText(text).catch(() => {
            const ta = document.createElement('textarea');
            ta.value = text;
            document.body.appendChild(ta);
            ta.select();
            document.execCommand('copy');
            document.body.removeChild(ta);
        });
    }

    // 为链接添加悬停监听
    function addHoverListeners() {
        document.querySelectorAll('a[href]').forEach(link => {
            if (link.__isparkHoverBound) return;
            link.__isparkHoverBound = true;

            link.addEventListener('mouseenter', (e) => {
                if (!hoverEnabled) return;
                const url = link.href;
                if (!isDownloadUrl(url)) return;

                const linkData = {
                    url: url,
                    fileName: getFileName(url),
                    fileSize: getFileSize(link)
                };

                showHoverPopup(linkData, link);
            });

            link.addEventListener('mouseleave', () => {
                hideHoverPopup();
            });
        });
    }

    // 初始添加悬停监听
    addHoverListeners();

    // 监听动态添加的元素，为新链接添加悬停监听
    const hoverObserver = new MutationObserver(() => {
        addHoverListeners();
    });
    hoverObserver.observe(document.body, {
        childList: true,
        subtree: true
    });

    // 监听来自 popup 的消息
    chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
        if (request.type === 'GET_DETECTED_LINKS') {
            const links = Array.from(detectedLinks.values());
            sendResponse({ links: links });
        } else if (request.type === 'GET_ALL_LINKS') {
            const allLinks = [];
            document.querySelectorAll('a[href]').forEach(link => {
                allLinks.push({
                    url: link.href,
                    text: link.textContent.trim(),
                    isDownload: isDownloadUrl(link.href)
                });
            });
            sendResponse({ links: allLinks });
        } else if (request.type === 'TOGGLE_HOVER') {
            hoverEnabled = request.enabled;
            sendResponse({ enabled: hoverEnabled });
        }
        return true;
    });

    console.log('[IsparkDownloader2] Content script loaded with hover support');
})();
