namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс сервиса для сохранения данных
    /// </summary>
    public interface IDataService
    {
        void SaveReceivedCodesToFile(List<string> receivedCodes);

        /// <summary>
        /// Сохраняет данные одного обработанного кода в отдельный файл.
        /// </summary>
        /// <param name="linearCode">Линейный код, используется как имя файла.</param>
        /// <param name="combinedData">Полная строка данных для сохранения.</param>
        void SaveSingleCode(string linearCode, string combinedData);
    }
}
