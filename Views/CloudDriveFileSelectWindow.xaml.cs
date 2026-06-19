using System.Windows;
using IsparkDownloader2.Core;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Views
{
    public partial class CloudDriveFileSelectWindow : Window
    {
        private readonly List<CloudDriveFileInfo> _files;
        private readonly CloudDriveShareInfo _shareInfo;
        private readonly CloudDriveType _driveType;
        private readonly TaskManager _taskManager;

        public CloudDriveFileSelectWindow(List<CloudDriveFileInfo> files, CloudDriveShareInfo shareInfo,
            CloudDriveType driveType, TaskManager taskManager, string defaultSavePath)
        {
            _files = files;
            _shareInfo = shareInfo;
            _driveType = driveType;
            _taskManager = taskManager;
            InitializeComponent();

            SavePathTextBox.Text = defaultSavePath;
            FileListView.ItemsSource = files;

            var totalSize = files.Where(f => !f.IsFolder).Sum(f => f.FileSize);
            var fileCount = files.Count(f => !f.IsFolder);
            StatusText.Text = $"共 {files.Count} 个项目，{fileCount} 个文件，总大小: {totalSize / 1024.0 / 1024.0:F2} MB";
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            FileListView.SelectAll();
        }

        private void DownloadSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = FileListView.SelectedItems.Cast<CloudDriveFileInfo>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("请先选择要下载的文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var savePath = SavePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(savePath))
            {
                MessageBox.Show("请输入保存路径", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fileItems = selectedItems.Where(f => !f.IsFolder).ToList();
            if (fileItems.Count == 0)
            {
                MessageBox.Show("选中的项目中没有可下载的文件", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var file in fileItems)
            {
                _taskManager.AddCloudDriveShareTask(
                    _shareInfo.ShareId,
                    file.FileName,
                    file.FileSize,
                    _driveType,
                    _shareInfo.ShareId,
                    _shareInfo.ShareToken,
                    file.FileId,
                    savePath
                );
            }

            MessageBox.Show($"已添加 {fileItems.Count} 个下载任务", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
