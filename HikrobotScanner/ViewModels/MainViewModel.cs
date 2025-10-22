using HikrobotScanner.Properties;
using HikrobotScanner.Services;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace HikrobotScanner.ViewModels
{
    /// <summary>
    /// Основная ViewModel. Содержит всю логику приложения и его состояние.
    /// </summary>
    public class MainViewModel : ViewModelBase
    {
        #region Сервисы
        private readonly CameraService _cameraService;
        private readonly ServerService _serverService;
        private readonly BarcodeService _barcodeService;
        private readonly DataService _dataService;
        private readonly SettingsService _settingsService;
        #endregion

        #region Свойства состояния (State)

        private Settings _settings;

        public Settings Settings
        {
            get => _settings;
            set => SetProperty(ref _settings, value);
        }

        private string _logText;
        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        private string _statusText = "Сервер не запущен.";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        private string _camera1Data;
        public string Camera1Data
        {
            get => _camera1Data;
            set => SetProperty(ref _camera1Data, value);
        }

        private string _camera2Data;
        public string Camera2Data
        {
            get => _camera2Data;
            set => SetProperty(ref _camera2Data, value);
        }

        private int _barcodeQuantity = 10;
        public int BarcodeQuantity
        {
            get => _barcodeQuantity;
            set => SetProperty(ref _barcodeQuantity, value);
        }

        private long _barcodeCounter;
        public long BarcodeCounter
        {
            get => _barcodeCounter;
            set
            {
                if (SetProperty(ref _barcodeCounter, value))
                {
                    OnPropertyChanged(nameof(BarcodeCounterDisplay));
                }
            }
        }

        public string BarcodeCounterDisplay => _barcodeCounter.ToString("D7");

        private bool _isServerRunning;
        public bool IsServerRunning
        {
            get => _isServerRunning;
            set
            {
                if (SetProperty(ref _isServerRunning, value))
                {
                    OnPropertyChanged(nameof(IsServerStopped));
                    (StartServerCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (StopServerCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsServerStopped => !IsServerRunning;

        // Буферы для данных с камер
        private string _camera1DataBuffer = null;
        private string _camera2DataBuffer = null;

        // Потокобезопасная коллекция для логов
        private readonly StringBuilder _logBuilder = new StringBuilder();

        // Список полученных кодов
        private readonly List<string> _receivedCodes = new List<string>();

        #endregion

        #region Команды (Commands)
        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand ClearDatabaseCommand { get; }
        public ICommand GenerateBarcodesCommand { get; }
        #endregion

        #region Конструктор
        public MainViewModel()
        {
            // Инициализация сервисов
            _settingsService = new SettingsService();
            _dataService = new DataService(Log);
            _barcodeService = new BarcodeService(Log, ShowError);
            _cameraService = new CameraService(Log);
            _serverService = new ServerService(Log, OnDataReceivedFromService);

            // Инициализация команд
            StartServerCommand = new RelayCommand(ExecuteStartServer, CanExecuteStartServer);
            StopServerCommand = new RelayCommand(ExecuteStopServer, CanExecuteStopServer);
            ClearDatabaseCommand = new RelayCommand(ExecuteClearDatabase);
            GenerateBarcodesCommand = new RelayCommand(ExecuteGenerateBarcodes);

            // Загрузка состояния
            LoadApplicationState();
        }
        #endregion

        #region Логика команд

        private bool CanExecuteStartServer(object obj) => IsServerStopped;
        private void ExecuteStartServer(object obj)
        {
            Log("Запуск сервера и инициализация камер...");

            // Получаем актуальные профили из настроек
            string userSet1 = $"UserSet{Settings.UserSetIndex + 1}";
            string userSet2 = $"UserSet{Settings.UserSetIndex2 + 1}";

            if (!int.TryParse(Settings.ListenPort, out int port1) || !int.TryParse(Settings.ListenPort2, out int port2))
            {
                Log($"Ошибка: Неверный формат портов в настройках ('{Settings.ListenPort}', '{Settings.ListenPort2}'). Сервер не запущен.");
                ShowError("Один или оба порта для прослушивания указаны неверно. Проверьте настройки.", "Ошибка настроек");
                return;
            }

            _cameraService.Initialize(userSet1, userSet2);
            _serverService.Start(port1, port2);

            IsServerRunning = true;
            StatusText = $"Сервер слушает порты {port1} & {port2}";
        }

        private bool CanExecuteStopServer(object obj) => IsServerRunning;
        private void ExecuteStopServer(object obj)
        {
            Log("Остановка сервера...");
            ShutdownServer();
        }

        private void ExecuteClearDatabase(object obj)
        {
            _receivedCodes.Clear();
            Log("База данных (в памяти) очищена.");
        }

        private void ExecuteGenerateBarcodes(object obj)
        {
            if (BarcodeQuantity <= 0)
            {
                ShowError("Введите корректное количество кодов (положительное число).", "Ошибка ввода");
                return;
            }

            Log($"Генерация {BarcodeQuantity} штрих-кодов...");
            var (success, nextCounter) = _barcodeService.GenerateAndPrintBarcodes(BarcodeCounter, BarcodeQuantity);

            if (success)
            {
                BarcodeCounter = nextCounter;
                _barcodeService.SaveCounter(BarcodeCounter);
            }
        }

        #endregion

        #region Обработка данных

        /// <summary>
        /// Этот метод вызывается из ServerService (из другого потока)
        /// </summary>
        private void OnDataReceivedFromService(int cameraNumber, string data)
        {
            // Используем Application.Current.Dispatcher для безопасного обновления UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (cameraNumber == 1)
                {
                    _camera1DataBuffer = data;
                    Camera1Data = data; // Обновляем свойство для UI
                    Log("Получены данные с камеры 1.");
                }
                else // cameraNumber == 2
                {
                    _camera2DataBuffer = data;
                    Camera2Data = data; // Обновляем свойство для UI
                    Log("Получены данные с камеры 2.");
                }

                // Проверяем, получены ли данные от ОБЕИХ камер
                if (_camera1DataBuffer != null && _camera2DataBuffer != null)
                {
                    Log("Получены данные с обеих камер, начинаю обработку.");
                    ProcessCombinedData(_camera1DataBuffer, _camera2DataBuffer);

                    // Очищаем буферы и UI для следующей пары
                    _camera1DataBuffer = null;
                    _camera2DataBuffer = null;
                    Camera1Data = "";
                    Camera2Data = "";
                }
            });
        }

        /// <summary>
        /// Вся бизнес-логика проверки кодов теперь находится здесь.
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
                ShowError("Линейный штрих-код не найден.", "Ошибка сканирования");
                return;
            }

            if (uniqueLinearCodes.Count > 1)
            {
                Log("Ошибка: Найдено несколько разных линейных штрих-кодов.");
                ShowError("Найдено несколько разных линейных штрих-кодов.", "Ошибка сканирования");
                return;
            }

            var finalLinearCode = uniqueLinearCodes.Single();
            if (_receivedCodes.Any(c => c.StartsWith(finalLinearCode + "|")))
            {
                Log($"Штрих-код {finalLinearCode} уже сохранен.");
                return;
            }

            // Получаем ожидаемое кол-во из настроек
            int expectedPartsCount = Settings.ExpectedPartsIndex == 0 ? 6 : 8;

            var uniqueQrCodes = qrCodes.Distinct().ToList();
            if (uniqueQrCodes.Count < expectedPartsCount)
            {
                Log($"Ошибка: Количество QR-кодов ({uniqueQrCodes.Count}) меньше ожидаемого ({expectedPartsCount}).");
                ShowError($"Недостаточно QR-кодов (Найдено: {uniqueQrCodes.Count}, Ожидалось: {expectedPartsCount}).", "Ошибка сканирования");
                return;
            }

            var codesToSave = new List<string> { finalLinearCode };
            codesToSave.AddRange(uniqueQrCodes);
            var dataToSave = string.Join("|", codesToSave);

            _receivedCodes.Add(dataToSave);
            Log($"Код успешно обработан и сохранен: {finalLinearCode}");
        }

        #endregion

        #region Вспомогательные методы (Логирование, Ошибки)

        /// <summary>
        /// Безопасный для потоков метод логирования.
        /// </summary>
        private void Log(string message)
        {
            // Если мы не в UI-потоке, делаем Invoke
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => Log(message));
                return;
            }

            _logBuilder.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            LogText = _logBuilder.ToString(); // Обновляем свойство, привязанное к UI
        }

        /// <summary>
        /// Отображение окна с ошибкой.
        /// </summary>
        private void ShowError(string message, string title)
        {
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() => ShowError(message, title));
                return;
            }

            MessageBox.Show(Application.Current.MainWindow,
                message,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        #endregion

        #region Управление жизненным циклом

        /// <summary>
        /// Загружает начальное состояние приложения.
        /// </summary>
        private void LoadApplicationState()
        {
            Settings = _settingsService.LoadSettings();
            Log("Настройки загружены.");
            BarcodeCounter = _barcodeService.LoadCounter();
            Log($"Счетчик штрих-кодов загружен: {BarcodeCounter}");
        }

        /// <summary>
        /// Выполняет всю логику остановки и сохранения.
        /// </summary>
        private void ShutdownServer()
        {
            _serverService.Stop();
            _cameraService.Cleanup();

            _dataService.SaveReceivedCodesToFile(_receivedCodes);
            _receivedCodes.Clear(); // Очищаем после сохранения

            IsServerRunning = false;
            StatusText = "Сервер остановлен.";
            Log("Сервер остановлен, ресурсы освобождены.");
        }

        /// <summary>
        /// Вызывается из Code-Behind при закрытии окна.
        /// </summary>
        public void OnWindowClosing()
        {
            Log("Приложение закрывается...");
            if (IsServerRunning)
            {
                ShutdownServer();
            }
            _settingsService.SaveSettings(Settings);
            _barcodeService.SaveCounter(BarcodeCounter);
            Log("Настройки и счетчик сохранены. Выход.");
        }

        #endregion
    }
}

