using System.IO;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис для сохранения данных в файл.
    /// </summary>
    public class DataService
    {
        private readonly Action<string> _logCallback;

        public DataService(Action<string> logCallback)
        {
            _logCallback = logCallback;
        }

        public void SaveReceivedCodesToFile(List<string> receivedCodes)
        {
            if (receivedCodes.Count == 0)
            {
                Log("Нет полученных кодов для сохранения.");
                return;
            }

            try
            {
                var directory = AppDomain.CurrentDomain.BaseDirectory;
                var fileName = $"ReceivedCodes_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(directory, fileName);

                File.WriteAllLines(filePath, receivedCodes);
                Log($"Сохранено {receivedCodes.Count} кодов в файл: {filePath}");
            }
            catch (Exception ex)
            {
                Log($"Ошибка сохранения файла: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            _logCallback?.Invoke(message);
        }
    }
}
