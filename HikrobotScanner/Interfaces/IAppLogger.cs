namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс для сервиса логирования
    /// </summary>
    public interface IAppLogger
    {
        /// <summary>
        /// Событие, срабатывающее при обновлении лога
        /// </summary>
        event Action<string> LogUpdated;

        /// <summary>
        /// Добавляет сообщение в лог
        /// </summary>
        void Log(string message);

        /// <summary>
        /// Возвращает весь текст лога
        /// </summary>
        string GetLogText();
    }
}
