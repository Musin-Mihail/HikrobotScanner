using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;

namespace HikrobotScanner
{
    public partial class MainWindow : Window
    {
        private TcpListener _tcpServer;
        private CancellationTokenSource _cancellationTokenSource;

        public MainWindow()
        {
            InitializeComponent();
            // Запускаем сервер для приема данных при старте приложения
            StartListeningServer();
        }

        /// <summary>
        /// Запускает TCP-сервер для прослушивания входящих данных от камеры.
        /// </summary>
        private void StartListeningServer()
        {
            if (!int.TryParse(ListenPortTextBox.Text, out int port))
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
                    // Ожидаем подключения клиента (камеры)
                    TcpClient client = await _tcpServer.AcceptTcpClientAsync();
                    Log("Камера подключилась для отправки данных.");

                    // Обрабатываем каждого клиента в отдельной задаче
                    _ = HandleClientComm(client, token);
                }
            }
            catch (SocketException ex)
            {
                // Исключение может возникнуть при остановке сервера
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
                    byte[] buffer = new byte[1024];
                    int bytesRead;

                    // Читаем данные, пока они есть и токен не отменен
                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                    {
                        string receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Log($"Получены данные: {receivedData}");

                        // Обновляем UI в основном потоке
                        Dispatcher.Invoke(() => { LastResultTextBox.Text = receivedData; });
                    }
                }
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
        /// Обработчик нажатия кнопки для отправки триггера.
        /// </summary>
        private async void SendTriggerButton_Click(object sender, RoutedEventArgs e)
        {
            string cameraIp = CameraIpTextBox.Text;
            if (!int.TryParse(TriggerPortTextBox.Text, out int triggerPort))
            {
                Log("Ошибка: Неверный формат порта триггера.");
                return;
            }

            string triggerCommand = TriggerCommandTextBox.Text;

            Log($"Отправка триггера '{triggerCommand}' на {cameraIp}:{triggerPort}...");

            try
            {
                // Создаем TCP-клиент для отправки команды
                using (var client = new TcpClient())
                {
                    // Устанавливаем таймаут подключения
                    var connectTask = client.ConnectAsync(cameraIp, triggerPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
                    {
                        // Подключение успешно
                        byte[] data = Encoding.UTF8.GetBytes(triggerCommand);
                        NetworkStream stream = client.GetStream();
                        await stream.WriteAsync(data, 0, data.Length);
                        Log("Триггер успешно отправлен.");
                    }
                    else
                    {
                        // Таймаут
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
        /// Логирует сообщение в текстовое поле и в консоль.
        /// </summary>
        private void Log(string message)
        {
            // Убеждаемся, что обновление UI происходит в основном потоке
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => Log(message));
                return;
            }

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
            LogTextBox.ScrollToEnd(); // Автопрокрутка
        }

        /// <summary>
        /// Корректно останавливает сервер при закрытии окна.
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            _tcpServer?.Stop();
            Log("Приложение закрывается...");
        }
    }
}