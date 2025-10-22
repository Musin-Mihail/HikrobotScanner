using HikrobotScanner.ViewModels;
using System.ComponentModel;
using System.Windows;

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
    }
}
