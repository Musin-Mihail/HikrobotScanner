namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс сервиса для сохранения данных
    /// </summary>
    public interface IDataService
    {
        void SaveReceivedCodesToFile(List<string> receivedCodes);
    }
}
