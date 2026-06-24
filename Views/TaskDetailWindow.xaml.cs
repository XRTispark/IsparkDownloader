using System.Windows;
using IsparkDownloader2.Models;

namespace IsparkDownloader2.Views
{
    public partial class TaskDetailWindow : Window
    {
        public TaskDetailWindow(DownloadTask task)
        {
            InitializeComponent();
            DataContext = task;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
