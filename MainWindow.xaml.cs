using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IsparkDownloader2.Models;
using IsparkDownloader2.ViewModels;
using IsparkDownloader2.Views;

namespace IsparkDownloader2
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void TaskItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is DownloadTask task)
            {
                var contextMenu = new ContextMenu();
                contextMenu.Items.Add(new MenuItem { Header = "查看详情", Command = new RelayCommand(() => OpenTaskDetail(task)) });
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(new MenuItem { Header = "开始", Command = new RelayCommand(() => StartTask(task)) });
                contextMenu.Items.Add(new MenuItem { Header = "暂停", Command = new RelayCommand(() => PauseTask(task)) });
                contextMenu.Items.Add(new MenuItem { Header = "删除", Command = new RelayCommand(() => DeleteTask(task)) });
                contextMenu.Items.Add(new Separator());
                contextMenu.Items.Add(new MenuItem { Header = "打开文件位置", Command = new RelayCommand(() => OpenFileLocation(task)) });

                contextMenu.PlacementTarget = element;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                contextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private void OpenTaskDetail(DownloadTask task)
        {
            var detailWindow = new TaskDetailWindow(task);
            detailWindow.Owner = this;
            detailWindow.ShowDialog();
        }

        private void StartTask(DownloadTask task)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedTask = task;
                vm.StartTaskCommand.Execute(task);
            }
        }

        private void PauseTask(DownloadTask task)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedTask = task;
                vm.PauseTaskCommand.Execute(task);
            }
        }

        private void DeleteTask(DownloadTask task)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SelectedTask = task;
                vm.RemoveTaskCommand.Execute(task);
            }
        }

        private void OpenFileLocation(DownloadTask task)
        {
            if (System.IO.Directory.Exists(task.SavePath))
            {
                System.Diagnostics.Process.Start("explorer.exe", task.SavePath);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Dispose();
            }
            base.OnClosing(e);
        }
    }

    public class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;

        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public event System.EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _execute();
        }
    }
}
