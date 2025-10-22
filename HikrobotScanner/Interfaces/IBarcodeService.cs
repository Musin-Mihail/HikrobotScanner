namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс сервиса для работы со штрих-кодами
    /// </summary>
    public interface IBarcodeService
    {
        long LoadCounter();
        void SaveCounter(long counter);
        /// <summary>
        /// Генерирует и отправляет на печать штрих-коды.
        /// Может выбросить исключение в случае ошибки печати.
        /// </summary>
        /// <returns>Возвращает (bool success, long nextCounter)</returns>
        (bool, long) GenerateAndPrintBarcodes(long startCounter, int quantity);
    }
}
