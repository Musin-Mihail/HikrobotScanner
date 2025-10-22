namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс сервиса для работы с камерами Hikrobot
    /// </summary>
    public interface ICameraService
    {
        void Initialize(string userSet1, string userSet2);
        void Cleanup();
    }
}
