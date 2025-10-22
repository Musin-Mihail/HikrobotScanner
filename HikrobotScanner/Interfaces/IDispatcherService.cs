namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс для выполнения операций в UI-потоке
    /// </summary>
    public interface IDispatcherService
    {
        void InvokeOnUIThread(Action action);
    }
}
