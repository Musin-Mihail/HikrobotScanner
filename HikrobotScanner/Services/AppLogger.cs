using HikrobotScanner.Interfaces;
using System.Text;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Потокобезопасная реализация сервиса логирования
    /// </summary>
    public class AppLogger : IAppLogger
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private readonly object _lock = new object();

        public event Action<string> LogUpdated;

        public void Log(string message)
        {
            string formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

            lock (_lock)
            {
                _logBuilder.AppendLine(formattedMessage);
            }

            LogUpdated?.Invoke(GetLogText());
        }

        public string GetLogText()
        {
            lock (_lock)
            {
                return _logBuilder.ToString();
            }
        }
    }
}
