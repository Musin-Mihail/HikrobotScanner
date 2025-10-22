using System.ComponentModel;
using System.Windows;

namespace HikrobotScanner;

/// <summary>
/// Основной файл класса MainWindow.
/// Содержит конструктор и обработчики событий пользовательского интерфейса.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadBarcodeCounter();
        UpdateCounterDisplay();
        LoadSettings();
    }

    private void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        InitializeCamera();
        StartListeningServer();
        StartServerButton.IsEnabled = false;
        StopServerButton.IsEnabled = true;
    }

    private void StopServerButton_Click(object sender, RoutedEventArgs e)
    {
        ShutdownServer();
    }

    /// <summary>
    /// Выполняет всю логику остановки сервера и очистки ресурсов.
    /// </summary>
    private void ShutdownServer()
    {
        _cancellationTokenSource?.Cancel();
        _tcpServer?.Stop();
        _tcpServer2?.Stop();

        SaveReceivedCodesToFile();
        CleanupCamera();

        StartServerButton.IsEnabled = true;
        StopServerButton.IsEnabled = false;
        StatusTextBlock.Text = "Сервер остановлен.";
        Log("Сервер остановлен, ресурсы освобождены.");
    }

    private void ClearDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        _receivedCodes.Clear();
        Log("База данных очищена.");
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(QuantityTextBox.Text, out var quantity) || quantity <= 0)
        {
            Log("Ошибка: Введите корректное количество кодов (положительное число).");
            MessageBox.Show("Введите корректное количество кодов.", "Ошибка ввода", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Log($"Генерация {quantity} штрих-кодов...");
        var barcodesToPrint = new List<string>();
        for (var i = 0; i < quantity; i++)
        {
            var barcode = $"{BarcodePrefix}{_barcodeCounter:D7}{BarcodeSuffix}";
            barcodesToPrint.Add(barcode);
            _barcodeCounter++;
        }

        Log($"Сгенерировано {barcodesToPrint.Count} кодов. Следующий код начнется с номера {_barcodeCounter}.");
        UpdateCounterDisplay();
        SaveBarcodeCounter();

        PrintBarcodes(barcodesToPrint);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        Log("Приложение закрывается...");
        SaveSettings();
        ShutdownServer();
        SaveBarcodeCounter();
    }
}
