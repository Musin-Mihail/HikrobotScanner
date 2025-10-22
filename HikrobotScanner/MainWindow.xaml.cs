using HikrobotScanner.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace HikrobotScanner
{
    /// <summary>
    /// Логика взаимодействия для MainWindow.xaml
    /// Code-behind теперь содержит только код, специфичный для View.
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            _viewModel = DataContext as MainViewModel;
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _viewModel?.OnWindowClosing();
        }
    }
}
