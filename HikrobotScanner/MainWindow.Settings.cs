using HikrobotScanner.Properties;

namespace HikrobotScanner;

/// <summary>
/// Логика для загрузки и сохранения настроек приложения.
/// </summary>
public partial class MainWindow
{
    private void LoadSettings()
    {
        CameraIpTextBox.Text = Settings.Default.CameraIp;
        TriggerPortTextBox.Text = Settings.Default.TriggerPort;
        ListenPortTextBox.Text = Settings.Default.ListenPort;
        UserSetComboBox.SelectedIndex = Settings.Default.UserSetIndex;

        CameraIpTextBox2.Text = Settings.Default.CameraIp2;
        TriggerPortTextBox2.Text = Settings.Default.TriggerPort2;
        ListenPortTextBox2.Text = Settings.Default.ListenPort2;
        UserSetComboBox2.SelectedIndex = Settings.Default.UserSetIndex2;

        ExpectedPartsComboBox.SelectedIndex = Settings.Default.ExpectedPartsIndex;

        Log("Настройки подключения успешно загружены.");
    }

    private void SaveSettings()
    {
        Settings.Default.CameraIp = CameraIpTextBox.Text;
        Settings.Default.TriggerPort = TriggerPortTextBox.Text;
        Settings.Default.ListenPort = ListenPortTextBox.Text;
        Settings.Default.UserSetIndex = UserSetComboBox.SelectedIndex;

        Settings.Default.CameraIp2 = CameraIpTextBox2.Text;
        Settings.Default.TriggerPort2 = TriggerPortTextBox2.Text;
        Settings.Default.ListenPort2 = ListenPortTextBox2.Text;
        Settings.Default.UserSetIndex2 = UserSetComboBox2.SelectedIndex;

        Settings.Default.ExpectedPartsIndex = ExpectedPartsComboBox.SelectedIndex;

        Settings.Default.Save();
        Log("Настройки подключения сохранены.");
    }
}
