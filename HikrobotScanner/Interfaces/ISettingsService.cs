using HikrobotScanner.Properties;

namespace HikrobotScanner.Interfaces
{
    /// <summary>
    /// Интерфейс сервиса настроек
    /// </summary>
    public interface ISettingsService
    {
        Settings LoadSettings();
        void SaveSettings(Settings settings);
    }
}
