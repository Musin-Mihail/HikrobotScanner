using HikrobotScanner.Interfaces;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис, инкапсулирующий логику TCP-сервера для приема данных.
    /// </summary>
    public class ServerService : IServerService
    {
        private readonly IAppLogger _logger;

        public Action<int, string> DataReceivedCallback { get; set; }

        private TcpListener _tcpServer1;
        private TcpListener _tcpServer2;
        private CancellationTokenSource _cancellationTokenSource;

        public ServerService(IAppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Запускает TCP-серверы для прослушивания входящих данных от камер.
        /// </summary>
        public void Start(int port1, int port2)
        {
            _cancellationTokenSource = new CancellationTokenSource();

            _tcpServer1 = new TcpListener(IPAddress.Any, port1);
            Task.Run(() => ListenForClients(_tcpServer1, 1, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _logger.Log($"Сервер для камеры 1 запущен. Ожидание данных на порту {port1}...");

            _tcpServer2 = new TcpListener(IPAddress.Any, port2);
            Task.Run(() => ListenForClients(_tcpServer2, 2, _cancellationTokenSource.Token), _cancellationTokenSource.Token);
            _logger.Log($"Сервер для камеры 2 запущен. Ожидание данных на порту {port2}...");
        }

        /// <summary>
        /// Останавливает оба TCP-сервера.
        /// </summary>
        public void Stop()
        {
            _cancellationTokenSource?.Cancel();
            _tcpServer1?.Stop();
            _tcpServer2?.Stop();
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
                    _logger.Log($"Камера {cameraNumber} подключилась для отправки данных.");
                    _ = HandleClientTask(client, cameraNumber, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _logger.Log($"Критическая ошибка сервера (Камера {cameraNumber}): {ex.Message}");
                }
            }
            finally
            {
                server?.Stop();
                _logger.Log($"Сервер для камеры {cameraNumber} остановлен.");
            }
        }

        /// <summary>
        /// Обрабатывает входящие данные от подключенного клиента (камеры) в фоновом потоке.
        /// </summary>
        private async Task HandleClientTask(TcpClient client, int cameraNumber, CancellationToken token)
        {
            try
            {
                using (var stream = client.GetStream())
                {
                    var buffer = new byte[1024];
                    int bytesRead;
                    while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                    {
                        var receivedData = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                        DataReceivedCallback?.Invoke(cameraNumber, receivedData);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                _logger.Log($"Соединение (Камера {cameraNumber}) было принудительно разорвано.");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка при чтении данных (Камера {cameraNumber}): {ex.Message}");
            }
            finally
            {
                client.Close();
                _logger.Log($"Камера {cameraNumber} отключилась.");
            }
        }
    }
}
