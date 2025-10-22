using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows.Controls;

namespace HikrobotScanner;

/// <summary>
/// Логика, связанная с TCP-сервером для приема данных.
/// </summary>
public partial class MainWindow
{
    private TcpListener _tcpServer;
    private TcpListener _tcpServer2;
    private CancellationTokenSource _cancellationTokenSource;

    private string _camera1DataBuffer = null;
    private string _camera2DataBuffer = null;

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
                _ = HandleClientTask(client, cameraNumber, token);
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
            if (!token.IsCancellationRequested)
            {
                Log($"Критическая ошибка сервера (Камера {cameraNumber}): {ex.Message}");
            }
        }
        finally
        {
            server?.Stop();
            Log($"Сервер для камеры {cameraNumber} остановлен.");
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

                    Dispatcher.Invoke(() => OnDataReceived(cameraNumber, receivedData));
                }
            }
        }
        catch (OperationCanceledException) { /* Игнорируем */ }
        catch (IOException ex) when (ex.InnerException is SocketException)
        {
            Log($"Соединение (Камера {cameraNumber}) было принудительно разорвано.");
        }
        catch (Exception ex)
        {
            Log($"Ошибка при чтении данных (Камера {cameraNumber}): {ex.Message}");
        }
        finally
        {
            client.Close();
            Log($"Камера {cameraNumber} отключилась.");
        }
    }

    /// <summary>
    /// Этот метод выполняется в UI-потоке и безопасно обрабатывает данные.
    /// Он сохраняет данные в буфер и проверяет, получены ли данные от обеих камер.
    /// </summary>
    private void OnDataReceived(int cameraNumber, string data)
    {
        if (cameraNumber == 1)
        {
            _camera1DataBuffer = data;
            Code1camera.Text = data;
            Log("Получены данные с камеры 1.");
        }
        else // cameraNumber == 2
        {
            _camera2DataBuffer = data;
            Code2camera.Text = data;
            Log("Получены данные с камеры 2.");
        }

        // Проверяем, получены ли данные от ОБЕИХ камер
        if (_camera1DataBuffer != null && _camera2DataBuffer != null)
        {
            Log("Получены данные с обеих камер, начинаю обработку.");

            // Обрабатываем комбинированные данные
            ProcessCombinedData(_camera1DataBuffer, _camera2DataBuffer);

            // Очищаем буферы и UI для следующей пары
            _camera1DataBuffer = null;
            _camera2DataBuffer = null;
            Code1camera.Text = "";
            Code2camera.Text = "";
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
        if (ExpectedPartsComboBox.SelectedItem is ComboBoxItem selectedItem)
        {
            int.TryParse(selectedItem.Content.ToString(), out expectedPartsCount);
        }
        if (expectedPartsCount == 0) expectedPartsCount = 6; // Default fallback

        var uniqueQrCodes = qrCodes.Distinct().ToList();
        if (uniqueQrCodes.Count < expectedPartsCount)
        {
            Log($"Ошибка: Количество QR-кодов ({uniqueQrCodes.Count}) меньше ожидаемого ({expectedPartsCount}).");
            ShowError($"Недостаточно QR-кодов (Найдено: {uniqueQrCodes.Count}, Ожидалось: {expectedPartsCount}).");
            return;
        }

        var codesToSave = new List<string> { finalLinearCode };
        codesToSave.AddRange(uniqueQrCodes);
        var dataToSave = string.Join("|", codesToSave);

        _receivedCodes.Add(dataToSave);
        Log($"Код успешно обработан и сохранен: {finalLinearCode}");
    }
}
