# IsparkDownloader2 浏览器插件

一键捕获页面中的下载链接，发送到 IsparkDownloader2 桌面应用进行高速下载。

## 功能特性

- **自动检测** - 智能识别页面中的下载链接（EXE、ZIP、PDF、MP4 等）
- **GitHub 加速** - 自动识别 GitHub Release 链接
- **网盘支持** - 支持百度网盘、阿里云盘、夸克网盘分享链接
- **批量下载** - 一键发送多个链接到下载器
- **链接复制** - 无法连接下载器时自动复制到剪贴板
- **深色主题** - 与 IsparkDownloader2 一致的暗色界面

## 安装方法

### Chrome / Edge

1. 打开浏览器扩展管理页面：
   - Chrome: `chrome://extensions/`
   - Edge: `edge://extensions/`
2. 开启右上角的"开发者模式"
3. 点击"加载已解压的扩展程序"
4. 选择 `BrowserExtension` 文件夹

### Firefox

1. 打开 `about:debugging`
2. 点击"此 Firefox"
3. 点击"临时加载附加组件"
4. 选择 `manifest.json` 文件

## 使用方法

1. 确保 IsparkDownloader2 桌面应用已安装并运行
2. 浏览包含下载链接的网页
3. 点击浏览器工具栏上的 IsparkDownloader2 图标
4. 在弹出窗口中查看检测到的链接
5. 点击下载按钮发送到下载器，或点击"全部下载"批量发送

## 文件结构

```
BrowserExtension/
├── manifest.json      # 插件配置
├── background.js      # Service Worker
├── content.js         # 内容脚本（注入页面）
├── popup.html         # 弹出窗口
├── popup.css          # 弹出窗口样式
├── popup.js           # 弹出窗口逻辑
├── welcome.html       # 欢迎页面
├── icons/             # 图标目录
│   ├── icon16.png
│   ├── icon32.png
│   ├── icon48.png
│   └── icon128.png
└── README.md
```

## 通信方式

插件通过以下方式与 IsparkDownloader2 桌面应用通信：

1. **Native Messaging** - 首选方式，需要配置 Native Messaging Host
2. **HTTP API** - 备用方式，通过 localhost:3721 发送请求
3. **剪贴板** - 最后备用，将链接复制到剪贴板

## 支持的文件类型

- 可执行文件：EXE、MSI、DMG、APK、IPA
- 压缩文件：ZIP、RAR、7Z、TAR、GZ、BZ2
- 文档：PDF、DOC、DOCX、XLS、XLSX、PPT、PPTX
- 媒体：MP3、MP4、AVI、MKV、MOV、WMV、FLV
- 图片：JPG、JPEG、PNG、GIF、BMP、WEBP、SVG
- 其他：ISO、DEB、RPM、TORRENT

## 注意事项

- 首次使用需要在浏览器中确认插件权限
- 如果下载器未运行，链接将复制到剪贴板
- 插件不会上传任何数据到服务器，所有处理在本地完成
