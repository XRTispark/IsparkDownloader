// background.js - Service Worker，处理插件与下载器通信

// 存储检测到的链接
let detectedLinks = [];

// 监听来自 content script 的消息
chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.type === 'LINK_DETECTED') {
        const link = request.data;
        // 去重
        const exists = detectedLinks.some(l => l.url === link.url);
        if (!exists) {
            detectedLinks.push(link);
            // 保持最多 100 条
            if (detectedLinks.length > 100) {
                detectedLinks = detectedLinks.slice(-100);
            }
        }
        sendResponse({ success: true });
    } else if (request.type === 'GET_ALL_LINKS') {
        sendResponse({ links: detectedLinks });
    } else if (request.type === 'CLEAR_LINKS') {
        detectedLinks = [];
        sendResponse({ success: true });
    } else if (request.type === 'SEND_TO_DOWNLOADER') {
        sendToDownloader(request.data)
            .then(result => sendResponse({ success: true, result }))
            .catch(error => sendResponse({ success: false, error: error.message }));
        return true; // 保持消息通道开放
    }
    return true;
});

// 发送下载任务到 IsparkDownloader2
async function sendToDownloader(task) {
    // 方法1：通过 Native Messaging 与下载器通信
    try {
        const response = await sendNativeMessage(task);
        return response;
    } catch (error) {
        console.log('Native messaging failed, trying alternative methods:', error);
    }

    // 方法2：通过本地 HTTP API（如果下载器开启了 HTTP 服务）
    try {
        const response = await fetch('http://localhost:3721/api/download', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(task)
        });
        return await response.json();
    } catch (error) {
        console.log('HTTP API failed:', error);
    }

    // 方法3：复制到剪贴板
    await copyToClipboard(task.url);
    throw new Error('无法连接到 IsparkDownloader2，链接已复制到剪贴板');
}

// Native Messaging 通信
function sendNativeMessage(message) {
    return new Promise((resolve, reject) => {
        chrome.runtime.sendNativeMessage('com.ispark.isparkdownloader2', message, (response) => {
            if (chrome.runtime.lastError) {
                reject(new Error(chrome.runtime.lastError.message));
            } else {
                resolve(response);
            }
        });
    });
}

// 复制到剪贴板
async function copyToClipboard(text) {
    try {
        await navigator.clipboard.writeText(text);
    } catch (err) {
        // 备用方案
        const textArea = document.createElement('textarea');
        textArea.value = text;
        document.body.appendChild(textArea);
        textArea.select();
        document.execCommand('copy');
        document.body.removeChild(textArea);
    }
}

// 监听下载事件
chrome.downloads.onCreated.addListener((downloadItem) => {
    // 可以在这里拦截浏览器下载，发送到 IsparkDownloader2
    console.log('Browser download detected:', downloadItem);
});

// 安装/更新时的处理
chrome.runtime.onInstalled.addListener((details) => {
    if (details.reason === 'install') {
        console.log('IsparkDownloader2 extension installed');
        // 打开欢迎页面
        chrome.tabs.create({
            url: chrome.runtime.getURL('welcome.html')
        });
    }
});

console.log('[IsparkDownloader2] Background service worker started');
