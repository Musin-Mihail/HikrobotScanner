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
    private readonly Random _random = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadBarcodeCounter();
        UpdateCounterDisplay();
    }

    private void StartServerButton_Click(object sender, RoutedEventArgs e)
    {
        StartListeningServer();
        StartServerButton.IsEnabled = false;
        StopServerButton.IsEnabled = true;
    }

    private void StopServerButton_Click(object sender, RoutedEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _tcpServer?.Stop();
        SaveReceivedCodesToFile();
        StartServerButton.IsEnabled = true;
        StopServerButton.IsEnabled = false;
        StatusTextBlock.Text = "Сервер остановлен.";
    }

    /// <summary>
    /// Обработчик нажатия кнопки для запуска конвейера и активации LineOut3.
    /// </summary>
    private async void StartPipelineButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandToCameraAsync("start");
    }

    /// <summary>
    /// Обработчик нажатия кнопки для остановки конвейера и активации LineOut4.
    /// </summary>
    private async void StopPipelineButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandToCameraAsync("stop");
    }

    /// <summary>
    /// Отправляет текстовую команду на IP-адрес и порт камеры.
    /// </summary>
    /// <param name="command">Текстовая команда для отправки (например, "start" или "stop").</param>
    private async Task SendCommandToCameraAsync(string command)
    {
        string cameraIp = "";
        int triggerPort = 0;

        await Dispatcher.InvokeAsync(() =>
        {
            cameraIp = CameraIpTextBox.Text;
            int.TryParse(TriggerPortTextBox.Text, out triggerPort);
        });

        if (triggerPort == 0)
        {
            Log("Ошибка: Неверный формат порта для команд.");
            return;
        }

        Log($"Отправка команды '{command}' на {cameraIp}:{triggerPort}...");
        try
        {
            using (var client = new TcpClient())
            {
                var connectTask = client.ConnectAsync(cameraIp, triggerPort);
                if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                {
                    await connectTask;
                    var data = Encoding.UTF8.GetBytes(command);
                    var stream = client.GetStream();
                    await stream.WriteAsync(data, 0, data.Length);
                    Log($"Команда '{command}' успешно отправлена.");
                }
                else
                {
                    Log("Ошибка: Не удалось подключиться к камере (таймаут).");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка при отправке команды '{command}': {ex.Message}");
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
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
        {
            // Это ожидаемое исключение при остановке сервера методом Stop().
        }
        catch (Exception ex)
        {
            Log($"Критическая ошибка сервера: {ex.Message}");
        }
        finally
        {
            _tcpServer?.Stop();
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
                while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                {
                    var receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var modifiedString = receivedData[2..];
                    Dispatcher.Invoke(() => { LastResultTextBox.Text = modifiedString; });

                    var parts = modifiedString.Split([";;"], StringSplitOptions.None);
                    if (parts.Length == 7)
                    {
                        var dataToSave = modifiedString;
                        if (dataToSave.Length > 2)
                        {
                            var charArray = dataToSave.ToCharArray();

                            for (var i = 0; i < 2; i++)
                            {
                                var randomIndex = _random.Next(0, charArray.Length);
                                var randomDigit = (char)('0' + _random.Next(0, 10));
                                charArray[randomIndex] = randomDigit;
                            }

                            dataToSave = new string(charArray);
                        }

                        _receivedCodes.Add(dataToSave);
                        Log("Код соответствует правилам и обработан.");
                    }
                    else
                    {
                        Log($"Код не соответствует правилам (ожидалось 7 блоков, получено {parts.Length}). Код не сохранен.");
                        await SendCommandToCameraAsync("stop");
                        Dispatcher.Invoke(() =>
                        {
                            MessageBox.Show(this,
                                "Конвейер остановлен. Уберите товар с конвейера и запустите триггер для продолжения работы.",
                                "Ошибка сканирования",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* Игнорируем */
        }
        catch (IOException ex) when (ex.InnerException is SocketException)
        {
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
            PagePadding = new Thickness(5),
            ColumnWidth = 2.5 * 96
        };

        var barcodeWriter = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions { Height = 80, Width = 300, Margin = 10 }
        };

        foreach (var barcodeValue in barcodes)
        {
            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 20, 0, 5) };
            var pixelData = barcodeWriter.Write(barcodeValue);
            var wpfBitmap = PixelDataToWriteableBitmap(pixelData);

            panel.Children.Add(new Image { HorizontalAlignment = HorizontalAlignment.Center, Source = wpfBitmap, Stretch = Stretch.None });
            panel.Children.Add(new TextBlock { Text = barcodeValue, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
            doc.Blocks.Add(new BlockUIContainer(panel));
        }

        Log("Отправка документа на печать...");
        printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"Печать {barcodes.Count} штрих-кодов");
        Log("Документ отправлен на принтер.");
    }

    private WriteableBitmap PixelDataToWriteableBitmap(PixelData pixelData)
    {
        var wpfBitmap = new WriteableBitmap(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgr32, null);
        wpfBitmap.WritePixels(new Int32Rect(0, 0, pixelData.Width, pixelData.Height), pixelData.Pixels, pixelData.Width * 4, 0);
        return wpfBitmap;
    }

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

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _cancellationTokenSource?.Cancel();
        _tcpServer?.Stop();
        SaveBarcodeCounter();
        SaveReceivedCodesToFile();
        Log("Приложение закрывается...");
    }

    private void LoadBarcodeCounter()
    {
        try
        {
            if (!File.Exists(CounterFileName)) return;
            var content = File.ReadAllText(CounterFileName);
            if (long.TryParse(content, out var savedCounter))
            {
                _barcodeCounter = savedCounter;
                Log($"Счетчик загружен: {_barcodeCounter}");
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка загрузки счетчика: {ex.Message}");
        }
    }

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

    private void UpdateCounterDisplay()
    {
        if (CounterTextBlock != null)
        {
            CounterTextBlock.Text = _barcodeCounter.ToString("D7");
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
}