namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс TCP-сервера для приема данных
    /// </summary>
    public interface IServerService
    {
        /// <summary>
        /// Колбэк, вызываемый при получении данных.
        /// Параметры: (int cameraNumber, string data)
        /// </summary>
        Action<int, string> DataReceivedCallback { get; set; }

        void Start(int port1, int port2);
        void Stop();
    }
}
