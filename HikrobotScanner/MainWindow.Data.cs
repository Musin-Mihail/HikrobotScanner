using System.IO;

namespace HikrobotScanner;

/// <summary>
/// Логика, связанная с хранением и сохранением полученных данных.
/// </summary>
public partial class MainWindow
{
    private readonly List<string> _receivedCodes = [];

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
