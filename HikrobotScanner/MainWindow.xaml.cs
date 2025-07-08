using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace HikrobotScanner;

public partial class MainWindow : Window
{
    private TcpListener _tcpServer;
    private CancellationTokenSource _cancellationTokenSource;

    private long _barcodeCounter = 1;
    private const string CounterFileName = "barcode_counter.txt";
    private readonly List<string> _receivedCodes = [];

    public MainWindow()
    {
        InitializeComponent();
        LoadBarcodeCounter();
        UpdateCounterDisplay();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartListeningServer();
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _tcpServer?.Stop();
        SaveReceivedCodesToFile();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusTextBlock.Text = "Сервер остановлен.";
    }

    /// <summary>
    /// Обработчик нажатия кнопки для отправки триггера.
    /// </summary>
    private async void SendTriggerButton_Click(object sender, RoutedEventArgs e)
    {
        var cameraIp = CameraIpTextBox.Text;
        if (!int.TryParse(TriggerPortTextBox.Text, out int triggerPort))
        {
            Log("Ошибка: Неверный формат порта триггера.");
            return;
        }

        var triggerCommand = TriggerCommandTextBox.Text;
        Log($"Отправка триггера '{triggerCommand}' на {cameraIp}:{triggerPort}...");
        try
        {
            using (var client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(cameraIp, triggerPort);
                if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                {
                    var data = Encoding.UTF8.GetBytes(triggerCommand);
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                    Log("Триггер успешно отправлен.");
                }
                else
                {
                    Log("Ошибка: Не удалось подключиться к камере (таймаут).");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка при отправке триггера: {ex.Message}");
        }
    }

    /// <summary>
    /// Запускает TCP-сервер для прослушивания входящих данных от камеры.
    /// </summary>
    private void StartListeningServer()
    {
        if (!int.TryParse(ListenPortTextBox.Text, out var port))
        {
            Log("Ошибка: Неверный формат локального порта.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _tcpServer = new TcpListener(IPAddress.Any, port);
        Task.Run(() => ListenForClients(_cancellationTokenSource.Token), _cancellationTokenSource.Token);

        Log($"Сервер запущен. Ожидание данных на порту {port}...");
        StatusTextBlock.Text = $"Сервер слушает порт {port}";
    }

    /// <summary>
    /// Асинхронный метод, который слушает входящие подключения в цикле.
    /// </summary>
    private async Task ListenForClients(CancellationToken token)
    {
        try
        {
            _tcpServer.Start();
            while (!token.IsCancellationRequested)
            {
                var client = await _tcpServer.AcceptTcpClientAsync(token);
                Log("Камера подключилась для отправки данных.");
                _ = HandleClientComm(client, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Это ожидаемое исключение при отмене токена, логировать не нужно.
        }
        catch (SocketException ex)
        {
            if (ex.SocketErrorCode != SocketError.Interrupted)
            {
                Log($"Ошибка сокета сервера: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка сервера: {ex.Message}");
        }
        finally
        {
            _tcpServer.Stop();
            Log("Сервер остановлен.");
        }
    }

    /// <summary>
    /// Обрабатывает входящие данные от подключенного клиента (камеры).
    /// </summary>
    private async Task HandleClientComm(TcpClient client, CancellationToken token)
    {
        try
        {
            using (var stream = client.GetStream())
            {
                var buffer = new byte[1024];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                {
                    var receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    Log($"Получены данные: {receivedData}");
                    Dispatcher.Invoke(() => { LastResultTextBox.Text = receivedData; });

                    var parts = receivedData.Split([";;"], StringSplitOptions.None);
                    if (parts.Length == 7)
                    {
                        _receivedCodes.Add(receivedData);
                        Log("Код соответствует правилам и сохранен.");
                    }
                    else
                    {
                        Log($"Код не соответствует правилам (ожидалось 7 блоков, получено {parts.Length}). Код не сохранен.");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Это ожидаемое исключение при отмене, логировать не нужно.
        }
        catch (IOException ex) when (ex.InnerException is SocketException)
        {
            // Это может произойти при резком закрытии соединения, не является критической ошибкой.
            Log("Соединение было принудительно разорвано.");
        }
        catch (Exception ex)
        {
            Log($"Ошибка при чтении данных: {ex.Message}");
        }
        finally
        {
            client.Close();
            Log("Камера отключилась.");
        }
    }

    private void SaveReceivedCodesToFile()
    {
        if (_receivedCodes.Count == 0)
        {
            Log("Нет полученных кодов для сохранения.");
            return;
        }

        try
        {
            var directory = AppDomain.CurrentDomain.BaseDirectory;
            var fileName = $"ReceivedCodes_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var filePath = Path.Combine(directory, fileName);

            File.WriteAllLines(filePath, _receivedCodes);
            Log($"Сохранено {_receivedCodes.Count} кодов в файл: {filePath}");
            _receivedCodes.Clear();
        }
        catch (Exception ex)
        {
            Log($"Ошибка сохранения файла: {ex.Message}");
        }
    }

    /// <summary>
    /// Логирует сообщение в текстовое поле и в консоль.
    /// </summary>
    private void Log(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Log(message));
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    /// <summary>
    /// Корректно останавливает сервер при закрытии окна.
    /// </summary>
    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _tcpServer?.Stop();
        SaveBarcodeCounter();
        Log("Приложение закрывается...");
    }

    /// <summary>
    /// Загружает последнее значение счетчика из файла.
    /// </summary>
    private void LoadBarcodeCounter()
    {
        try
        {
            if (!File.Exists(CounterFileName)) return;
            var content = File.ReadAllText(CounterFileName);
            if (!long.TryParse(content, out var savedCounter)) return;
            _barcodeCounter = savedCounter;
            Log($"Счетчик загружен: {_barcodeCounter}");
        }
        catch (Exception ex)
        {
            Log($"Ошибка загрузки счетчика: {ex.Message}");
        }
    }

    /// <summary>
    /// Сохраняет текущее значение счетчика в файл.
    /// </summary>
    private void SaveBarcodeCounter()
    {
        try
        {
            File.WriteAllText(CounterFileName, _barcodeCounter.ToString());
        }
        catch (Exception ex)
        {
            Log($"Ошибка сохранения счетчика: {ex.Message}");
        }
    }

    /// <summary>
    /// Обновляет текстовый блок, отображающий текущий счетчик.
    /// </summary>
    private void UpdateCounterDisplay()
    {
        if (CounterTextBlock != null)
        {
            CounterTextBlock.Text = _barcodeCounter.ToString("D7");
        }
    }

    /// <summary>
    /// Обработчик нажатия кнопки для генерации и печати кодов.
    /// </summary>
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
            var barcode = $"004466005944{_barcodeCounter:D7}9";
            barcodesToPrint.Add(barcode);
            _barcodeCounter++;
        }

        Log($"Сгенерировано {barcodesToPrint.Count} кодов. Следующий код начнется с номера {_barcodeCounter}.");
        UpdateCounterDisplay();
        SaveBarcodeCounter();

        PrintBarcodes(barcodesToPrint);
    }

    /// <summary>
    /// Отправляет сгенерированные штрих-коды на печать.
    /// </summary>
    private void PrintBarcodes(List<string> barcodes)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            Log("Печать отменена пользователем.");
            return;
        }

        var doc = new FlowDocument
        {
            PageWidth = 2.5 * 96,
            PageHeight = 1.5 * 96,
            PagePadding = new Thickness(5)
        };
        doc.ColumnWidth = doc.PageWidth;

        var barcodeWriter = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Height = 80,
                Width = 300,
                Margin = 10
            }
        };

        foreach (var barcodeValue in barcodes)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(0, 20, 0, 5)
            };
            var pixelData = barcodeWriter.Write(barcodeValue);
            var wpfBitmap = PixelDataToWriteableBitmap(pixelData);

            var barcodeImage = new Image
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Source = wpfBitmap,
                Stretch = Stretch.None
            };
            var barcodeText = new TextBlock
            {
                Text = barcodeValue,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12
            };
            panel.Children.Add(barcodeImage);
            panel.Children.Add(barcodeText);

            doc.Blocks.Add(new BlockUIContainer(panel));
        }

        Log("Отправка документа на печать...");
        printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"Печать {barcodes.Count} штрих-кодов");
        Log("Документ отправлен на принтер.");
    }

    /// <summary>
    /// Конвертирует PixelData из ZXing.Net в WriteableBitmap для WPF.
    /// </summary>
    private WriteableBitmap PixelDataToWriteableBitmap(PixelData pixelData)
    {
        var wpfBitmap = new WriteableBitmap(
            pixelData.Width,
            pixelData.Height,
            96,
            96,
            PixelFormats.Bgr32,
            null);
        wpfBitmap.WritePixels(
            new Int32Rect(0, 0, pixelData.Width, pixelData.Height),
            pixelData.Pixels,
            pixelData.Width * 4,
            0);
        return wpfBitmap;
    }
}