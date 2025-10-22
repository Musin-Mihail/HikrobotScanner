using MvCamCtrl.NET;
using System.Runtime.InteropServices;
using System.Windows.Controls;

namespace HikrobotScanner;

/// <summary>
/// Логика, связанная с управлением камерами Hikrobot.
/// </summary>
public partial class MainWindow
{
    private readonly List<MyCamera> _cameras = [];

    /// <summary>
    /// Инициализация и подключение к камерам.
    /// </summary>
    private void InitializeCamera()
    {
        if (_cameras.Count > 0)
        {
            Log("Камеры уже подключены.");
            return;
        }

        CleanupCamera();

        MyCamera.MV_CC_DEVICE_INFO_LIST stDevList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
        int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDevList);
        if (nRet != MyCamera.MV_OK)
        {
            Log("Ошибка: Не удалось перечислить устройства.");
            return;
        }

        if (stDevList.nDeviceNum < 2)
        {
            Log($"Ошибка: Найдено {stDevList.nDeviceNum} камер, но требуется 2.");
            return;
        }

        Log($"Найдено {stDevList.nDeviceNum} камер. Подключение к первым двум...");

        for (int i = 0; i < 2; i++)
        {
            var camera = new MyCamera();
            MyCamera.MV_CC_DEVICE_INFO stDevInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(stDevList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));

            string cameraName = "Безымянная камера";
            try
            {
                if (stDevInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    // 1. Получаем байты для GigE устройства
                    byte[] gigeInfoBytes = stDevInfo.SpecialInfo.stGigEInfo;
                    // 2. Преобразуем байты в нужную структуру
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(gigeInfoBytes, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    // 3. Получаем имя
                    cameraName = gigeInfo.chUserDefinedName;
                }
                else if (stDevInfo.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    // То же самое для USB-устройства
                    byte[] usbInfoBytes = stDevInfo.SpecialInfo.stUsb3VInfo;
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(usbInfoBytes, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    cameraName = usbInfo.chUserDefinedName;
                }
            }
            catch (Exception ex)
            {
                Log($"Не удалось прочитать имя камеры {i + 1}: {ex.Message}");
            }

            if (string.IsNullOrEmpty(cameraName))
            {
                cameraName = $"Камера {i + 1} (без имени)";
            }

            Log($"Попытка подключения к камере: {cameraName}");

            nRet = camera.MV_CC_CreateDevice_NET(ref stDevInfo);
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось создать экземпляр для камеры {cameraName}. Код: {nRet:X}");
                continue;
            }

            nRet = camera.MV_CC_OpenDevice_NET();
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось открыть камеру {cameraName}. Код: {nRet:X}");
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }

            ComboBox userSetComboBox = (i == 0) ? UserSetComboBox : UserSetComboBox2;
            string selectedUserSet = ((ComboBoxItem)userSetComboBox.SelectedItem).Content.ToString();
            Log($"Загрузка настроек из {selectedUserSet} для камеры {cameraName}...");

            nRet = camera.MV_CC_SetEnumValueByString_NET("UserSetSelector", selectedUserSet);
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось выбрать {selectedUserSet} для камеры {cameraName}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }

            nRet = camera.MV_CC_SetCommandValue_NET("UserSetLoad");
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось загрузить настройки из {selectedUserSet} для камеры {cameraName}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }
            Log($"Настройки из {selectedUserSet} успешно загружены для камеры {cameraName}.");

            nRet = camera.MV_CC_StartGrabbing_NET();
            if (nRet != MyCamera.MV_OK)
            {
                Log($"Ошибка: Не удалось начать захват изображений для камеры {cameraName}. Код: {nRet:X}");
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
                continue;
            }

            Log($"Захват изображений запущен для камеры {cameraName}.");
            _cameras.Add(camera);
        }

        if (_cameras.Count > 0)
        {
            Log($"Успешно подключено {_cameras.Count} камер(ы) через SDK.");
        }
        else
        {
            Log("Не удалось подключить ни одной камеры.");
        }
    }


    /// <summary>
    /// Освобождение ресурсов камер.
    /// </summary>
    private void CleanupCamera()
    {
        if (_cameras.Count == 0) return;

        Log("Освобождение ресурсов камер...");
        foreach (var camera in _cameras)
        {
            camera.MV_CC_StopGrabbing_NET();
            camera.MV_CC_CloseDevice_NET();
            camera.MV_CC_DestroyDevice_NET();
        }
        _cameras.Clear();
        Log("Все камеры отключены и ресурсы освобождены.");
    }
}
