using HikrobotScanner.Properties;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис для управления настройками приложения.
    /// Он инкапсулирует логику работы с Properties.Settings.Default.
    /// 
    /// Класс сделан 'internal', чтобы соответствовать 'internal' классу Settings.
    /// </summary>
    internal class SettingsService
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

