using HikrobotScanner.Interfaces;
using System.IO;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис для сохранения данных в файл.
    /// </summary>
    public class DataService : IDataService
    {
        private readonly IAppLogger _logger;
        private const string SingleSaveDirectory = "codes";

        public DataService(IAppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Сохраняет данные одного обработанного кода в отдельный файл в папку 'codes'.
        /// </summary>
        public void SaveSingleCode(string linearCode, string combinedData)
        {
            try
            {
                var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SingleSaveDirectory);
                Directory.CreateDirectory(directory);

                var fileName = $"{DateTime.Now:yyyyMMdd_HHmmss}_{linearCode}.txt";
                var filePath = Path.Combine(directory, fileName);

                File.WriteAllText(filePath, combinedData);
                _logger.Log($"Код {linearCode} сохранен в файл: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка сохранения файла для кода {linearCode}: {ex.Message}");
            }
        }

        public void SaveReceivedCodesToFile(List<string> receivedCodes)
        {
            if (receivedCodes.Count == 0)
            {
                _logger.Log("Нет полученных кодов для сохранения.");
                return;
            }

            try
            {
                var directory = AppDomain.CurrentDomain.BaseDirectory;
                var fileName = $"ReceivedCodes_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                var filePath = Path.Combine(directory, fileName);

                File.WriteAllLines(filePath, receivedCodes);
                _logger.Log($"Сохранено {receivedCodes.Count} кодов в файл: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Log($"Ошибка сохранения файла: {ex.Message}");
            }
        }
    }
}
