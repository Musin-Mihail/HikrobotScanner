using HikrobotScanner.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace HikrobotScanner
{
    /// <summary>
    /// Code-behind теперь еще чище.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            (DataContext as MainViewModel)?.OnWindowClosing();
        }
        private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LogTextBox.ScrollToEnd();
        }
    }
}
