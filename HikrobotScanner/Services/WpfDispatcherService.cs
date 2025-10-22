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
            if (Application.Current.Dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                Application.Current.Dispatcher.Invoke(action);
            }
        }
    }
}
