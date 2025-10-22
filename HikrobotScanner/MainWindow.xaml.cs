using HikrobotScanner.Properties;
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
    private TcpListener _tcpServer2;
    private CancellationTokenSource _cancellationTokenSource;

    // Изменено для поддержки нескольких камер
    private readonly List<MyCamera> _cameras = [];
    private bool AreCamerasConnected => _cameras.Count > 0;

    private long _barcodeCounter = 1;
    private const string CounterFileName = "barcode_counter.txt";
    private readonly List<string> _receivedCodes = [];
    private readonly Random _random = new();

    private string _firstCameraDataBuffer = null;

    public MainWindow()
    {
        InitializeComponent();
        LoadBarcodeCounter();
        UpdateCounterDisplay();
        LoadSettings();
    }
    private void LoadSettings()
    {
        CameraIpTextBox.Text = Settings.Default.CameraIp;
        TriggerPortTextBox.Text = Settings.Default.TriggerPort;
        ListenPortTextBox.Text = Settings.Default.ListenPort;
        UserSetComboBox.SelectedIndex = Settings.Default.UserSetIndex;

        CameraIpTextBox2.Text = Settings.Default.CameraIp2;
        TriggerPortTextBox2.Text = Settings.Default.TriggerPort2;
        ListenPortTextBox2.Text = Settings.Default.ListenPort2;
        UserSetComboBox2.SelectedIndex = Settings.Default.UserSetIndex2;

        ExpectedPartsComboBox.SelectedIndex = Settings.Default.ExpectedPartsIndex;

        Log("Настройки подключения успешно загружены.");
    }
    private void SaveSettings()
    {
        Settings.Default.CameraIp = CameraIpTextBox.Text;
        Settings.Default.TriggerPort = TriggerPortTextBox.Text;
        Settings.Default.ListenPort = ListenPortTextBox.Text;
        Settings.Default.UserSetIndex = UserSetComboBox.SelectedIndex;

        Settings.Default.CameraIp2 = CameraIpTextBox2.Text;
        Settings.Default.TriggerPort2 = TriggerPortTextBox2.Text;
        Settings.Default.ListenPort2 = ListenPortTextBox2.Text;
        Settings.Default.UserSetIndex2 = UserSetComboBox2.SelectedIndex;

        Settings.Default.ExpectedPartsIndex = ExpectedPartsComboBox.SelectedIndex;

        Settings.Default.Save();
        Log("Настройки подключения сохранены.");
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
        _tcpServer2?.Stop();
        SaveReceivedCodesToFile();
        CleanupCamera();
        StartServerButton.IsEnabled = true;
        StopServerButton.IsEnabled = false;
        StatusTextBlock.Text = "Сервер остановлен.";
    }

    /// <summary>
    /// Запускает TCP-сервер для прослушивания входящих данных от камер.
    /// </summary>
    private void StartListeningServer()
    {
        if (!int.TryParse(ListenPortTextBox.Text, out var port1) || !int.TryParse(ListenPortTextBox2.Text, out var port2))
        {
            Log("Ошибка: Неверный формат локального порта.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        // Listener for Camera 1
        _tcpServer = new TcpListener(IPAddress.Any, port1);
        Task.Run(() => ListenForClients(_tcpServer, 1, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
        Log($"Сервер для камеры 1 запущен. Ожидание данных на порту {port1}...");
        // Listener for Camera 2
        _tcpServer2 = new TcpListener(IPAddress.Any, port2);
        Task.Run(() => ListenForClients(_tcpServer2, 2, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
        Log($"Сервер для камеры 2 запущен. Ожидание данных на порту {port2}...");
        StatusTextBlock.Text = $"Сервер слушает порты {port1} & {port2}";
    }

    /// <summary>
    /// Асинхронный метод, который слушает входящие подключения в цикле.
    /// </summary>
    private async Task ListenForClients(TcpListener server, int cameraNumber, CancellationToken token)
    {
        try
        {
            server.Start();
            while (!token.IsCancellationRequested)
            {
                var client = await server.AcceptTcpClientAsync(token);
                Log($"Камера {cameraNumber} подключилась для отправки данных.");
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
            server?.Stop();
            Log($"Сервер для камеры {cameraNumber} остановлен.");
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
                    lock (this)
                    {
                        if (_firstCameraDataBuffer == null)
                        {

                            // Data from the first camera arrived
                            _firstCameraDataBuffer = receivedData;
                            Dispatcher.Invoke(() => { Code1camera.Text = receivedData.Trim(); });
                            Log("Получены данные с первой камеры, ожидание данных со второй.");
                        }
                        else
                        {
                            // Data from the second camera arrived, process both
                            Log("Получены данные со второй камеры, начинаю обработку.");
                            Dispatcher.Invoke(() => { Code2camera.Text = receivedData.Trim(); });
                            ProcessCombinedData(_firstCameraDataBuffer, receivedData);
                            _firstCameraDataBuffer = null; // Reset buffer for the next pair
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* Игнорируем */ }
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

    /// <summary>
    /// Processes and validates data from both cameras.
    /// </summary>
    private void ProcessCombinedData(string data1, string data2)
    {
        var combinedData = $"{data1.Trim()}|{data2.Trim()}";


        var allParts = combinedData.Split(new[] { "|" }, StringSplitOptions.RemoveEmptyEntries);

        var linearCodes = new List<string>();
        var qrCodes = new List<string>();

        foreach (var part in allParts)
        {
            if (part.Length == 20 && long.TryParse(part, out _))
            {
                linearCodes.Add(part);
            }
            else
            {
                qrCodes.Add(part);
            }
        }

        var uniqueLinearCodes = linearCodes.Distinct().ToList();
        if (uniqueLinearCodes.Count == 0)
        {
            Log("Ошибка: Линейный штрих-код не найден.");
            ShowError("Линейный штрих-код не найден.");
            return;
        }

        if (uniqueLinearCodes.Count > 1)
        {
            Log("Ошибка: Найдено несколько разных линейных штрих-кодов.");
            ShowError("Найдено несколько разных линейных штрих-кодов.");
            return;
        }

        var finalLinearCode = uniqueLinearCodes.Single();
        if (_receivedCodes.Any(c => c.StartsWith(finalLinearCode + "|")))
        {
            Log($"Штрих-код {finalLinearCode} уже сохранен.");
            return;
        }

        int expectedPartsCount = 0;
        Dispatcher.Invoke(() =>
        {
            if (ExpectedPartsComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                int.TryParse(selectedItem.Content.ToString(), out expectedPartsCount);
            }
        });
        if (expectedPartsCount == 0) expectedPartsCount = 6; // Default fallback

        var uniqueQrCodes = qrCodes.Distinct().ToList();
        if (uniqueQrCodes.Count < expectedPartsCount)
        {
            Log($"Ошибка: Количество QR-кодов ({uniqueQrCodes.Count}) меньше ожидаемого ({expectedPartsCount}).");
            ShowError($"Недостаточно QR-кодов (Найдено: {uniqueQrCodes.Count}, Ожидалось: {expectedPartsCount}).");
            return;
        }

        // Success: save the codes
        var codesToSave = new List<string> { finalLinearCode };
        codesToSave.AddRange(uniqueQrCodes);
        var dataToSave = string.Join("|", codesToSave);

        _receivedCodes.Add(dataToSave);
        Log($"Код успешно обработан и сохранен: {finalLinearCode}");
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
        SaveSettings();
        _cancellationTokenSource?.Cancel();
        _tcpServer?.Stop();
        _tcpServer2?.Stop();
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
    /// Инициализация и подключение к камерам.
    /// </summary>
    private void InitializeCamera()
    {
        if (AreCamerasConnected)
        {
            Log("Камеры уже подключены.");
            return;
        }

        CleanupCamera();

        MyCamera.MV_CC_DEVICE_INFO_LIST stDevList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
        int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDevList);
        if (nRet != MyCamera.MV_OK)
        {
            Log("Ошибка: Не удалось перечислить устройства.");
            return;
        }

        if (stDevList.nDeviceNum < 2)
        {
            Log($"Ошибка: Найдено {stDevList.nDeviceNum} камер, но требуется 2.");
            return;
        }

        Log($"Найдено {stDevList.nDeviceNum} камер. Подключение к первым двум...");

        for (int i = 0; i < 2; i++)
        {
            var camera = new MyCamera();
            MyCamera.MV_CC_DEVICE_INFO stDevInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(stDevList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));

            string cameraName = "Безымянная камера";
            try
            {
                if (stDevInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    // 1. Получаем байты для GigE устройства
                    byte[] gigeInfoBytes = stDevInfo.SpecialInfo.stGigEInfo;
                    // 2. Преобразуем байты в нужную структуру
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(gigeInfoBytes, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    // 3. Получаем имя
                    cameraName = gigeInfo.chUserDefinedName;
                }
                else if (stDevInfo.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    // То же самое для USB-устройства
                    byte[] usbInfoBytes = stDevInfo.SpecialInfo.stUsb3VInfo;
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(usbInfoBytes, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    cameraName = usbInfo.chUserDefinedName;
                }
            }
            catch (Exception ex)
            {
                Log($"Не удалось прочитать имя камеры {i + 1}: {ex.Message}");
            }

            if (string.IsNullOrEmpty(cameraName))
            {
                cameraName = $"Камера {i + 1} (без имени)";
            }

            Log($"Попытка подключения к камере: {cameraName}");

            nRet = camera.MV_CC_CreateDevice_NET(ref stDevInfo);
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось создать экземпляр для камеры {cameraName}. Код: {nRet:X}");
                continue;
            }

            nRet = camera.MV_CC_OpenDevice_NET();
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось открыть камеру {cameraName}. Код: {nRet:X}");
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }

            ComboBox userSetComboBox = (i == 0) ? UserSetComboBox : UserSetComboBox2;
            string selectedUserSet = ((ComboBoxItem)userSetComboBox.SelectedItem).Content.ToString();
            Log($"Загрузка настроек из {selectedUserSet} для камеры {cameraName}...");

            nRet = camera.MV_CC_SetEnumValueByString_NET("UserSetSelector", selectedUserSet);
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось выбрать {selectedUserSet} для камеры {cameraName}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }

            nRet = camera.MV_CC_SetCommandValue_NET("UserSetLoad");
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось загрузить настройки из {selectedUserSet} для камеры {cameraName}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }
            Log($"Настройки из {selectedUserSet} успешно загружены для камеры {cameraName}.");

            nRet = camera.MV_CC_StartGrabbing_NET();
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось начать захват изображений для камеры {cameraName}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }

            Log($"Захват изображений запущен для камеры {cameraName}.");
            _cameras.Add(camera);
        }

        if (AreCamerasConnected)
        {
            Log($"Успешно подключено {_cameras.Count} камер(ы) через SDK.");
        }
        else
        {
            Log("Не удалось подключить ни одной камеры.");
        }
    }


    /// <summary>
    /// Освобождение ресурсов камер.
    /// </summary>
    private void CleanupCamera()
    {
        if (!AreCamerasConnected) return;

        Log("Освобождение ресурсов камер...");
        foreach (var camera in _cameras)
        {
            camera.MV_CC_StopGrabbing_NET();
            camera.MV_CC_CloseDevice_NET();
            camera.MV_CC_DestroyDevice_NET();
        }
        _cameras.Clear(); // Очищаем список после освобождения ресурсов
        Log("Все камеры отключены и ресурсы освобождены.");
    }
}