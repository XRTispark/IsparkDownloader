// popup.js - 插件弹出窗口逻辑

document.addEventListener('DOMContentLoaded', async () => {
    // Tab 切换
    const tabBtns = document.querySelectorAll('.tab-btn');
    const tabContents = document.querySelectorAll('.tab-content');

    tabBtns.forEach(btn => {
        btn.addEventListener('click', () => {
            const tab = btn.dataset.tab;

            tabBtns.forEach(b => b.classList.remove('active'));
            tabContents.forEach(c => c.classList.remove('active'));

            btn.classList.add('active');
            document.getElementById(tab + 'Tab').classList.add('active');
        });
    });

    // 刷新按钮
    document.getElementById('refreshBtn').addEventListener('click', loadLinks);

    // 清空按钮
    document.getElementById('clearBtn').addEventListener('click', async () => {
        await chrome.runtime.sendMessage({ type: 'CLEAR_LINKS' });
        loadLinks();
        showToast('已清空列表', 'success');
    });

    // 全部下载按钮
    document.getElementById('downloadAllBtn').addEventListener('click', async () => {
        const response = await chrome.runtime.sendMessage({ type: 'GET_ALL_LINKS' });
        const links = response.links || [];

        if (links.length === 0) {
            showToast('没有可下载的链接', 'error');
            return;
        }

        let success = 0;
        let failed = 0;

        for (const link of links) {
            try {
                await sendToDownloader(link);
                success++;
            } catch (error) {
                failed++;
                console.error('Download failed:', error);
            }
        }

        showToast(`发送完成: ${success} 成功, ${failed} 失败`, success > 0 ? 'success' : 'error');
    });

    // 加载设置
    const settings = await chrome.storage.local.get(['autoDownload', 'interceptDownload', 'hoverEnabled']);
    document.getElementById('autoDownload').checked = settings.autoDownload || false;
    document.getElementById('interceptDownload').checked = settings.interceptDownload !== false;
    document.getElementById('hoverDownload').checked = settings.hoverEnabled !== false;

    // 保存设置
    document.getElementById('autoDownload').addEventListener('change', (e) => {
        chrome.storage.local.set({ autoDownload: e.target.checked });
    });

    document.getElementById('interceptDownload').addEventListener('change', (e) => {
        chrome.storage.local.set({ interceptDownload: e.target.checked });
    });

    document.getElementById('hoverDownload').addEventListener('change', async (e) => {
        const enabled = e.target.checked;
        chrome.storage.local.set({ hoverEnabled: enabled });

        // 通知所有标签页更新悬停设置
        const tabs = await chrome.tabs.query({});
        for (const tab of tabs) {
            try {
                await chrome.tabs.sendMessage(tab.id, { type: 'TOGGLE_HOVER', enabled });
            } catch {
                // 忽略无法通信的标签页
            }
        }
    });

    // 初始加载
    loadLinks();
});

// 加载链接列表
async function loadLinks() {
    // 加载检测到的链接
    const detectedResponse = await chrome.runtime.sendMessage({ type: 'GET_ALL_LINKS' });
    const detectedLinks = detectedResponse.links || [];
    renderDetectedLinks(detectedLinks);

    // 加载所有链接（从当前页面）
    try {
        const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
        if (tab) {
            const response = await chrome.tabs.sendMessage(tab.id, { type: 'GET_ALL_LINKS' });
            renderAllLinks(response?.links || []);
        }
    } catch (error) {
        console.log('Could not get all links:', error);
        renderAllLinks([]);
    }
}

// 渲染检测到的链接
function renderDetectedLinks(links) {
    const container = document.getElementById('detectedList');

    if (links.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                    <polyline points="7 10 12 15 17 10"></polyline>
                    <line x1="12" y1="15" x2="12" y2="3"></line>
                </svg>
                <p>未检测到下载链接</p>
                <span>浏览包含下载文件的页面时，链接将自动显示在这里</span>
            </div>
        `;
        return;
    }

    container.innerHTML = links.map(link => createLinkItem(link, true)).join('');

    // 绑定下载按钮事件
    container.querySelectorAll('.btn-download').forEach(btn => {
        btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const url = btn.dataset.url;
            const link = links.find(l => l.url === url);
            if (link) {
                try {
                    await sendToDownloader(link);
                    showToast('已发送到下载器', 'success');
                } catch (error) {
                    showToast(error.message, 'error');
                }
            }
        });
    });

    // 绑定复制按钮事件
    container.querySelectorAll('.btn-copy').forEach(btn => {
        btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const url = btn.dataset.url;
            try {
                await navigator.clipboard.writeText(url);
                showToast('链接已复制', 'success');
            } catch {
                showToast('复制失败', 'error');
            }
        });
    });
}

// 渲染所有链接
function renderAllLinks(links) {
    const container = document.getElementById('allList');

    if (links.length === 0) {
        container.innerHTML = `
            <div class="empty-state">
                <p>当前页面没有链接</p>
            </div>
        `;
        return;
    }

    // 只显示可能是下载的链接，或前50个
    const downloadLinks = links.filter(l => l.isDownload);
    const displayLinks = downloadLinks.length > 0 ? downloadLinks : links.slice(0, 50);

    container.innerHTML = displayLinks.map(link => createLinkItem({
        url: link.url,
        fileName: link.text || getFileName(link.url),
        fileSize: '',
        source: 'page'
    }, false)).join('');

    // 绑定事件
    container.querySelectorAll('.btn-download').forEach(btn => {
        btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const url = btn.dataset.url;
            try {
                await sendToDownloader({ url, fileName: getFileName(url) });
                showToast('已发送到下载器', 'success');
            } catch (error) {
                showToast(error.message, 'error');
            }
        });
    });

    container.querySelectorAll('.btn-copy').forEach(btn => {
        btn.addEventListener('click', async (e) => {
            e.stopPropagation();
            const url = btn.dataset.url;
            try {
                await navigator.clipboard.writeText(url);
                showToast('链接已复制', 'success');
            } catch {
                showToast('复制失败', 'error');
            }
        });
    });
}

// 创建链接项 HTML
function createLinkItem(link, showSource) {
    const ext = getFileExtension(link.fileName);
    const sizeText = link.fileSize ? ` · ${link.fileSize}` : '';
    const sourceText = showSource && link.source ? ` · ${link.source}` : '';

    return `
        <div class="link-item" data-url="${escapeHtml(link.url)}">
            <div class="link-icon">${ext}</div>
            <div class="link-info">
                <div class="link-name">${escapeHtml(link.fileName)}</div>
                <div class="link-url">${escapeHtml(link.url)}</div>
                <div class="link-meta">${escapeHtml(sizeText)}${escapeHtml(sourceText)}</div>
            </div>
            <div class="link-actions">
                <button class="btn-icon btn-copy" data-url="${escapeHtml(link.url)}" title="复制链接">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
                        <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
                    </svg>
                </button>
                <button class="btn-icon download btn-download" data-url="${escapeHtml(link.url)}" title="下载">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
                        <polyline points="7 10 12 15 17 10"></polyline>
                        <line x1="12" y1="15" x2="12" y2="3"></line>
                    </svg>
                </button>
            </div>
        </div>
    `;
}

// 发送下载任务
async function sendToDownloader(link) {
    return new Promise((resolve, reject) => {
        chrome.runtime.sendMessage({
            type: 'SEND_TO_DOWNLOADER',
            data: {
                url: link.url,
                fileName: link.fileName,
                fileSize: link.fileSize,
                pageUrl: link.pageUrl || '',
                pageTitle: link.pageTitle || ''
            }
        }, (response) => {
            if (chrome.runtime.lastError) {
                reject(new Error(chrome.runtime.lastError.message));
            } else if (response && response.success) {
                resolve(response.result);
            } else {
                reject(new Error(response?.error || '发送失败'));
            }
        });
    });
}

// 获取文件扩展名
function getFileExtension(fileName) {
    if (!fileName) return '?';
    const match = fileName.match(/\.([a-zA-Z0-9]+)$/);
    return match ? match[1].substring(0, 3) : '?';
}

// 从 URL 获取文件名
function getFileName(url) {
    try {
        const urlObj = new URL(url);
        const pathname = decodeURIComponent(urlObj.pathname);
        return pathname.split('/').pop() || '未知文件';
    } catch {
        return '未知文件';
    }
}

// HTML 转义
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// 显示 Toast
function showToast(message, type = 'info') {
    // 移除已有的 toast
    document.querySelectorAll('.toast').forEach(t => t.remove());

    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
    document.body.appendChild(toast);

    setTimeout(() => {
        toast.style.opacity = '0';
        toast.style.transition = 'opacity 0.3s';
        setTimeout(() => toast.remove(), 300);
    }, 2000);
}
