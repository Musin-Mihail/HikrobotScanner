using HikrobotScanner.Interfaces;
using System.Windows;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Реализация IDispatcherService для WPF
    /// </summary>
    public class WpfDispatcherService : IDispatcherService
    {
        public void InvokeOnUIThread(Action action)
        {
            // Если мы уже в UI-потоке, просто выполняем
            if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else // Иначе, используем Dispatcher
            {
                Application.Current.Dispatcher.Invoke(action);
            }
        }
    }
}
