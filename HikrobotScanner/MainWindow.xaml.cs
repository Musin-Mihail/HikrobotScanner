using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MvCamCtrl.NET;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace HikrobotScanner;

public partial class MainWindow : Window
{
    private TcpListener _tcpServer;
    private CancellationTokenSource _cancellationTokenSource;

    private MyCamera _camera;
    private bool _isCameraConnected;

    private long _barcodeCounter = 1;
    private const string CounterFileName = "barcode_counter.txt";
    private readonly List<string> _receivedCodes = [];
    private readonly Random _random = new();

    public MainWindow()
    {
        InitializeComponent();
        LoadBarcodeCounter();
        UpdateCounterDisplay();
        _camera = new MyCamera();
        _isCameraConnected = false;
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
        _cancellationTokenSource?.Cancel();
        _tcpServer?.Stop();
        SaveReceivedCodesToFile();
        CleanupCamera();
        StartServerButton.IsEnabled = true;
        StopServerButton.IsEnabled = false;
        StatusTextBlock.Text = "Сервер остановлен.";
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


                    // Получаем ожидаемое количество блоков из ComboBox в потоке UI
                    int expectedPartsCount = 0;
                    Dispatcher.Invoke(() =>
                    {
                        if (ExpectedPartsComboBox.SelectedItem is ComboBoxItem selectedItem)
                        {
                            int.TryParse(selectedItem.Content.ToString(), out expectedPartsCount);
                        }
                    });

                    // Если по какой-то причине значение не получено, используем 7 по умолчанию
                    if (expectedPartsCount == 0)
                    {
                        expectedPartsCount = 7;
                    }

                    var parts = modifiedString.Split([";;"], StringSplitOptions.None);
                    if (parts.Length == expectedPartsCount)
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
                        Log("Код соответствует правилам и обработан");
                    }
                    else
                    {
                        Log($"Код не соответствует правилам (ожидалось {expectedPartsCount} блоков, получено {parts.Length}). Код не сохранен.");
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
        CleanupCamera();
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

    /// <summary>
    /// Инициализация и подключение к камере.
    /// </summary>
    private void InitializeCamera()
    {
        if (_isCameraConnected)
        {
            return;
        }

        MyCamera.MV_CC_DEVICE_INFO_LIST stDevList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
        int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDevList);
        if (nRet != MyCamera.MV_OK)
        {
            Log("Ошибка: Не удалось перечислить устройства.");
            return;
        }

        if (stDevList.nDeviceNum == 0)
        {
            Log("Ошибка: Камеры не найдены.");
            return;
        }

        MyCamera.MV_CC_DEVICE_INFO stDevInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(stDevList.pDeviceInfo[0], typeof(MyCamera.MV_CC_DEVICE_INFO));

        nRet = _camera.MV_CC_CreateDevice_NET(ref stDevInfo);
        if (nRet != MyCamera.MV_OK)
        {
            Log($"Ошибка: Не удалось создать экземпляр камеры. Код: {nRet:X}");
            return;
        }

        nRet = _camera.MV_CC_OpenDevice_NET();
        if (nRet != MyCamera.MV_OK)
        {
            Log($"Ошибка: Не удалось открыть камеру. Код: {nRet:X}");
            _camera.MV_CC_DestroyDevice_NET();
            return;
        }

        // Получаем выбранный UserSet из ComboBox
        string selectedUserSet = ((ComboBoxItem)UserSetComboBox.SelectedItem).Content.ToString();

        Log($"Загрузка настроек из {selectedUserSet}...");

        // 1. Выбираем UserSet на основе выбора пользователя
        nRet = _camera.MV_CC_SetEnumValueByString_NET("UserSetSelector", selectedUserSet);
        if (nRet != MyCamera.MV_OK)
        {
            Log($"Ошибка: Не удалось выбрать {selectedUserSet}. Код: {nRet:X}");
            _camera.MV_CC_CloseDevice_NET();
            _camera.MV_CC_DestroyDevice_NET();
            return;
        }

        // 2. Выполняем команду загрузки
        nRet = _camera.MV_CC_SetCommandValue_NET("UserSetLoad");
        if (nRet != MyCamera.MV_OK)
        {
            Log($"Ошибка: Не удалось загрузить настройки из {selectedUserSet}. Код: {nRet:X}");
            _camera.MV_CC_CloseDevice_NET();
            _camera.MV_CC_DestroyDevice_NET();
            return;
        }

        Log($"Настройки из {selectedUserSet} успешно загружены.");

        // 3. Запуск захвата изображений (перевод камеры в режим Normal)
        nRet = _camera.MV_CC_StartGrabbing_NET();
        if (nRet != MyCamera.MV_OK)
        {
            Log($"Ошибка: Не удалось начать захват изображений. Код: {nRet:X}");
            _camera.MV_CC_CloseDevice_NET();
            _camera.MV_CC_DestroyDevice_NET();
            return;
        }

        Log("Захват изображений запущен (режим Normal).");

        _isCameraConnected = true;
        Log("Камера успешно подключена через SDK.");
    }

    /// <summary>
    /// Освобождение ресурсов камеры.
    /// </summary>
    private void CleanupCamera()
    {
        if (!_isCameraConnected || _camera == null) return;
        _camera.MV_CC_StopGrabbing_NET();
        Log("Захват изображений остановлен.");
        _camera.MV_CC_CloseDevice_NET();
        _camera.MV_CC_DestroyDevice_NET();
        _isCameraConnected = false;
        Log("Соединение с камерой через SDK закрыто.");
    }
}