using System;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Views
{
    public partial class TrackerConfigWindow : Window
    {
        private readonly TrackerConfigManager _manager;

        public TrackerConfigWindow(TrackerConfigManager manager)
        {
            InitializeComponent();
            _manager = manager;
            RefreshTrackers();
        }

        private void RefreshTrackers()
        {
            try
            {
                var firstCategory = _manager.Categories.FirstOrDefault();
                if (firstCategory != null)
                {
                    AllTrackersListBox.ItemsSource = firstCategory.Trackers.ToList();
                }
                else
                {
                    AllTrackersListBox.ItemsSource = _manager.GetAllTrackers().ToList();
                }

                CustomTrackersListBox.ItemsSource = _manager.CustomTrackers.ToList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"刷新 Tracker 列表失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddTracker_Click(object sender, RoutedEventArgs e)
        {
            var url = NewTrackerTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("请输入 Tracker URL", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var firstCategory = _manager.Categories.FirstOrDefault();
                if (firstCategory != null)
                {
                    _manager.AddTrackerToCategory(firstCategory.Name, url);
                }
                else
                {
                    _manager.AddCustomTracker(url);
                }

                NewTrackerTextBox.Clear();
                RefreshTrackers();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddCustomTracker_Click(object sender, RoutedEventArgs e)
        {
            var url = CustomTrackerTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("请输入 Tracker URL", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _manager.AddCustomTracker(url);
                CustomTrackerTextBox.Clear();
                RefreshTrackers();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"添加失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>从 TXT 文件批量导入 Tracker 列表</summary>
        private void ImportTxt_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 Tracker 列表文件",
                Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var lines = File.ReadAllLines(dialog.FileName);
                var validUrls = new List<string>();
                var skipped = 0;

                foreach (var line in lines)
                {
                    var url = line.Trim();
                    if (string.IsNullOrEmpty(url)) continue;

                    // 跳过注释行
                    if (url.StartsWith("#") || url.StartsWith("//")) continue;

                    // 验证是否为有效 URL
                    if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                        url.StartsWith("udp://", StringComparison.OrdinalIgnoreCase))
                    {
                        validUrls.Add(url);
                    }
                    else
                    {
                        skipped++;
                    }
                }

                if (validUrls.Count == 0)
                {
                    MessageBox.Show("文件中未找到有效的 Tracker URL", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 批量添加到第一个分类
                var firstCategory = _manager.Categories.FirstOrDefault();
                if (firstCategory != null)
                {
                    foreach (var url in validUrls)
                    {
                        _manager.AddTrackerToCategory(firstCategory.Name, url);
                    }
                }
                else
                {
                    foreach (var url in validUrls)
                    {
                        _manager.AddCustomTracker(url);
                    }
                }

                var msg = $"成功导入 {validUrls.Count} 个 Tracker";
                if (skipped > 0)
                    msg += $"，跳过 {skipped} 个无效行";

                MessageBox.Show(msg, "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshTrackers();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (AllTrackersListBox.SelectedItem is string allItem)
                {
                    var firstCategory = _manager.Categories.FirstOrDefault();
                    if (firstCategory != null)
                    {
                        _manager.RemoveTrackerFromCategory(firstCategory.Name, allItem);
                    }
                }
                else if (CustomTrackersListBox.SelectedItem is string customItem)
                {
                    _manager.RemoveCustomTracker(customItem);
                }

                RefreshTrackers();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}