using System.Windows;

namespace HikrobotScanner;

/// <summary>
/// Вспомогательные методы, такие как логирование и отображение ошибок.
/// </summary>
public partial class MainWindow
{
    private void Log(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Log(message));
            return;
        }

        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void ShowError(string message)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(this,
                $"{message}",
                "Ошибка сканирования",
                MessageBoxButton.OK,
                     MessageBoxImage.Warning);
        });
    }
}
