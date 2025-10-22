using HikrobotScanner.Interfaces;
using HikrobotScanner.Properties;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис для управления настройками приложения.
    /// Он инкапсулирует логику работы с Properties.Settings.Default.
    /// 
    /// Класс теперь public, чтобы его можно было внедрить через DI.
    /// </summary>
    public class SettingsService : ISettingsService
    {
        public Settings LoadSettings()
        {
            return Settings.Default;
        }

        public void SaveSettings(Settings settings)
        {
            Settings.Default.Save();
        }
    }
}
