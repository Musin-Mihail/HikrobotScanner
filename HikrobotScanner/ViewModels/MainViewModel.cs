using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HikrobotScanner.Interfaces;
using HikrobotScanner.Properties;
using System.Windows;

namespace HikrobotScanner.ViewModels
{
    /// <summary>
    /// Основная ViewModel. Теперь использует CommunityToolkit.Mvvm (ObservableObject)
    /// и получает все зависимости (сервисы) через конструктор (DI).
    /// </summary>
    public partial class MainViewModel : ObservableObject
    {
        #region Сервисы (получены через DI)
        private readonly ICameraService _cameraService;
        private readonly IServerService _serverService;
        private readonly IBarcodeService _barcodeService;
        private readonly IDataService _dataService;
        private readonly ISettingsService _settingsService;
        private readonly IAppLogger _logger;
        private readonly IDispatcherService _dispatcher;
        #endregion

        #region Свойства состояния (State)

        // [ObservableProperty] автоматически создает public свойство "Settings"
        // с вызовом OnPropertyChanged
        [ObservableProperty]
        private Settings _settings;

        [ObservableProperty]
        private string _logText;

        [ObservableProperty]
        private string _statusText = "Сервер не запущен.";

        [ObservableProperty]
        private string _camera1Data;

        [ObservableProperty]
        private string _camera2Data;

        [ObservableProperty]
        private int _barcodeQuantity = 10;

        // Для этого свойства нам нужна кастомная логика
        private long _barcodeCounter;
        public long BarcodeCounter
        {
            get => _barcodeCounter;
            set
            {
                // Используем SetProperty из ObservableObject
                if (SetProperty(ref _barcodeCounter, value))
                {
                    // Вызываем OnPropertyChanged для зависимого свойства
                    OnPropertyChanged(nameof(BarcodeCounterDisplay));
                }
            }
        }

        public string BarcodeCounterDisplay => _barcodeCounter.ToString("D7");

        [ObservableProperty]
        // [NotifyCanExecuteChangedFor] автоматически вызывает .NotifyCanExecuteChanged()
        // для указанных команд при изменении этого свойства.
        [NotifyCanExecuteChangedFor(nameof(StartServerCommand))]
        [NotifyCanExecuteChangedFor(nameof(StopServerCommand))]
        private bool _isServerRunning;

        public bool IsServerStopped => !IsServerRunning;

        // Буферы для данных с камер
        private string _camera1DataBuffer = null;
        private string _camera2DataBuffer = null;

        // Список полученных кодов
        private readonly List<string> _receivedCodes = new List<string>();

        #endregion

        #region Конструктор (Внедрение зависимостей)
        public MainViewModel(
            ICameraService cameraService,
            IServerService serverService,
            IBarcodeService barcodeService,
            IDataService dataService,
            ISettingsService settingsService,
            IAppLogger logger,
            IDispatcherService dispatcher)
        {
            // Сохраняем все внедренные сервисы
            _cameraService = cameraService;
            _serverService = serverService;
            _barcodeService = barcodeService;
            _dataService = dataService;
            _settingsService = settingsService;
            _logger = logger;
            _dispatcher = dispatcher;

            // Настраиваем колбэки и подписки
            _serverService.DataReceivedCallback = OnDataReceivedFromService;
            _logger.LogUpdated += (logText) => _dispatcher.InvokeOnUIThread(() => LogText = logText);

            // Загрузка состояния
            LoadApplicationState();
        }
        #endregion

        #region Команды (Commands)

        // [RelayCommand] атрибут автоматически создает IRelayCommand
        // с именем "StartServerCommand"
        [RelayCommand(CanExecute = nameof(CanStartServer))]
        private void StartServer()
        {
            _logger.Log("Запуск сервера и инициализация камер...");

            string userSet1 = $"UserSet{Settings.UserSetIndex + 1}";
            string userSet2 = $"UserSet{Settings.UserSetIndex2 + 1}";

            if (!int.TryParse(Settings.ListenPort, out int port1) || !int.TryParse(Settings.ListenPort2, out int port2))
            {
                string errorMsg = $"Ошибка: Неверный формат портов в настройках ('{Settings.ListenPort}', '{Settings.ListenPort2}'). Сервер не запущен.";
                _logger.Log(errorMsg);
                ShowError("Один или оба порта для прослушивания указаны неверно. Проверьте настройки.", "Ошибка настроек");
                return;
            }

            _cameraService.Initialize(userSet1, userSet2);
            _serverService.Start(port1, port2);

            IsServerRunning = true;
            StatusText = $"Сервер слушает порты {port1} & {port2}";
        }
        private bool CanStartServer() => IsServerStopped;

        [RelayCommand(CanExecute = nameof(CanStopServer))]
        private void StopServer()
        {
            _logger.Log("Остановка сервера...");
            ShutdownServer();
        }
        private bool CanStopServer() => IsServerRunning;

        [RelayCommand]
        private void ClearDatabase()
        {
            _receivedCodes.Clear();
            _logger.Log("База данных (в памяти) очищена.");
        }

        [RelayCommand]
        private void GenerateBarcodes()
        {
            if (BarcodeQuantity <= 0)
            {
                ShowError("Введите корректное количество кодов (положительное число).", "Ошибка ввода");
                return;
            }

            _logger.Log($"Генерация {BarcodeQuantity} штрих-кодов...");
            try
            {
                // Теперь мы отлавливаем исключение, которое может прийти из BarcodeService
                var (success, nextCounter) = _barcodeService.GenerateAndPrintBarcodes(BarcodeCounter, BarcodeQuantity);

                if (success)
                {
                    BarcodeCounter = nextCounter;
                    _barcodeService.SaveCounter(BarcodeCounter);
                }
            }
            catch (Exception ex)
            {
                // ViewModel отвечает за отображение ошибки пользователю
                _logger.Log($"Критическая ошибка печати: {ex.Message}");
                ShowError($"Не удалось выполнить печать. Убедитесь, что принтер подключен и готов.\n\nОшибка: {ex.Message}", "Ошибка печати");
            }
        }

        #endregion

        #region Обработка данных

        /// <summary>
        /// Этот метод вызывается из ServerService (из другого потока)
        /// </summary>
        private void OnDataReceivedFromService(int cameraNumber, string data)
        {
            // Используем IDispatcherService для безопасного обновления UI
            _dispatcher.InvokeOnUIThread(() =>
            {
                if (cameraNumber == 1)
                {
                    _camera1DataBuffer = data;
                    Camera1Data = data; // Обновляем свойство (автоматически вызовет OnPropertyChanged)
                    _logger.Log("Получены данные с камеры 1.");
                }
                else // cameraNumber == 2
                {
                    _camera2DataBuffer = data;
                    Camera2Data = data; // Обновляем свойство
                    _logger.Log("Получены данные с камеры 2.");
                }

                if (_camera1DataBuffer != null && _camera2DataBuffer != null)
                {
                    _logger.Log("Получены данные с обеих камер, начинаю обработку.");
                    ProcessCombinedData(_camera1DataBuffer, _camera2DataBuffer);

                    _camera1DataBuffer = null;
                    _camera2DataBuffer = null;
                    Camera1Data = "";
                    Camera2Data = "";
                }
            });
        }

        /// <summary>
        /// Вся бизнес-логика проверки кодов
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
                _logger.Log("Ошибка: Линейный штрих-код не найден.");
                ShowError("Линейный штрих-код не найден.", "Ошибка сканирования");
                return;
            }

            if (uniqueLinearCodes.Count > 1)
            {
                _logger.Log("Ошибка: Найдено несколько разных линейных штрих-кодов.");
                ShowError("Найдено несколько разных линейных штрих-кодов.", "Ошибка сканирования");
                return;
            }

            var finalLinearCode = uniqueLinearCodes.Single();
            if (_receivedCodes.Any(c => c.StartsWith(finalLinearCode + "|")))
            {
                _logger.Log($"Штрих-код {finalLinearCode} уже сохранен.");
                return;
            }

            int expectedPartsCount = Settings.ExpectedPartsIndex == 0 ? 6 : 8;

            var uniqueQrCodes = qrCodes.Distinct().ToList();
            if (uniqueQrCodes.Count < expectedPartsCount)
            {
                _logger.Log($"Ошибка: Количество QR-кодов ({uniqueQrCodes.Count}) меньше ожидаемого ({expectedPartsCount}).");
                ShowError($"Недостаточно QR-кодов (Найдено: {uniqueQrCodes.Count}, Ожидалось: {expectedPartsCount}).", "Ошибка сканирования");
                return;
            }

            var codesToSave = new List<string> { finalLinearCode };
            codesToSave.AddRange(uniqueQrCodes);
            var dataToSave = string.Join("|", codesToSave);

            _receivedCodes.Add(dataToSave);
            _logger.Log($"Код успешно обработан и сохранен: {finalLinearCode}");
        }

        #endregion

        #region Вспомогательные методы (Ошибки)

        /// <summary>
        /// Отображение окна с ошибкой.
        /// Этот метод должен быть в UI-слое (ViewModel), а не в сервисах.
        /// </summary>
        private void ShowError(string message, string title)
        {
            // Мы должны быть в UI-потоке, чтобы показать MessageBox
            _dispatcher.InvokeOnUIThread(() =>
            {
                MessageBox.Show(Application.Current.MainWindow,
                    message,
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        #endregion

        #region Управление жизненным циклом

        private void LoadApplicationState()
        {
            Settings = _settingsService.LoadSettings();
            _logger.Log("Настройки загружены.");
            BarcodeCounter = _barcodeService.LoadCounter();
            _logger.Log($"Счетчик штрих-кодов загружен: {BarcodeCounter}");
        }

        private void ShutdownServer()
        {
            _serverService.Stop();
            _cameraService.Cleanup();

            _dataService.SaveReceivedCodesToFile(_receivedCodes);
            _receivedCodes.Clear();

            IsServerRunning = false;
            StatusText = "Сервер остановлен.";
            _logger.Log("Сервер остановлен, ресурсы освобождены.");
        }

        /// <summary>
        /// Вызывается из Code-Behind при закрытии окна.
        /// </summary>
        public void OnWindowClosing()
        {
            _logger.Log("Приложение закрывается...");
            if (IsServerRunning)
            {
                ShutdownServer();
            }
            _settingsService.SaveSettings(Settings);
            _barcodeService.SaveCounter(BarcodeCounter);
            _logger.Log("Настройки и счетчик сохранены. Выход.");
        }

        #endregion
    }
}
