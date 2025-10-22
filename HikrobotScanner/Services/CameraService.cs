using HikrobotScanner.Interfaces;
using MvCamCtrl.NET;
using System.Runtime.InteropServices;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис, инкапсулирующий всю логику работы с SDK камер Hikrobot.
    /// </summary>
    public class CameraService : ICameraService
    {
        private readonly IAppLogger _logger;
        private readonly List<MyCamera> _cameras = new List<MyCamera>();

        public CameraService(IAppLogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Инициализация и подключение к камерам.
        /// </summary>
        public void Initialize(string userSet1, string userSet2)
        {
            if (_cameras.Count > 0)
            {
                _logger.Log("Камеры уже подключены.");
                return;
            }

            Cleanup();

            MyCamera.MV_CC_DEVICE_INFO_LIST stDevList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
            int nRet = MyCamera.MV_CC_EnumDevices_NET(MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref stDevList);
            if (nRet != MyCamera.MV_OK)
            {
                _logger.Log("Ошибка: Не удалось перечислить устройства.");
                return;
            }

            if (stDevList.nDeviceNum < 2)
            {
                _logger.Log($"Ошибка: Найдено {stDevList.nDeviceNum} камер, но требуется 2.");
                return;
            }

            _logger.Log($"Найдено {stDevList.nDeviceNum} камер. Подключение к первым двум...");

            string[] userSets = { userSet1, userSet2 };

            for (int i = 0; i < 2; i++)
            {
                var camera = new MyCamera();
                MyCamera.MV_CC_DEVICE_INFO stDevInfo = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(stDevList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO));

                string cameraName = GetCameraName(stDevInfo, i);
                _logger.Log($"Попытка подключения к камере: {cameraName}");

                nRet = camera.MV_CC_CreateDevice_NET(ref stDevInfo);
                if (nRet != MyCamera.MV_OK)
                {
                    _logger.Log($"Ошибка: Не удалось создать экземпляр для камеры {cameraName}. Код: {nRet:X}");
                    continue;
                }

                nRet = camera.MV_CC_OpenDevice_NET();
                if (nRet != MyCamera.MV_OK)
                {
                    _logger.Log($"Ошибка: Не удалось открыть камеру {cameraName}. Код: {nRet:X}");
                    camera.MV_CC_DestroyDevice_NET();
                    continue;
                }

                string selectedUserSet = userSets[i];
                _logger.Log($"Загрузка настроек из {selectedUserSet} для камеры {cameraName}...");

                nRet = camera.MV_CC_SetEnumValueByString_NET("UserSetSelector", selectedUserSet);
                if (nRet != MyCamera.MV_OK)
                {
                    _logger.Log($"Ошибка: Не удалось выбрать {selectedUserSet} для камеры {cameraName}. Код: {nRet:X}");
                    camera.MV_CC_CloseDevice_NET();
                    camera.MV_CC_DestroyDevice_NET();
                    continue;
                }

                nRet = camera.MV_CC_SetCommandValue_NET("UserSetLoad");
                if (nRet != MyCamera.MV_OK)
                {
                    _logger.Log($"Ошибка: Не удалось загрузить настройки из {selectedUserSet} для камеры {cameraName}. Код: {nRet:X}");
                    camera.MV_CC_CloseDevice_NET();
                    camera.MV_CC_DestroyDevice_NET();
                    continue;
                }
                _logger.Log($"Настройки из {selectedUserSet} успешно загружены для камеры {cameraName}.");

                nRet = camera.MV_CC_StartGrabbing_NET();
                if (nRet != MyCamera.MV_OK)
                {
                    _logger.Log($"Ошибка: Не удалось начать захват изображений для камеры {cameraName}. Код: {nRet:X}");
                    camera.MV_CC_CloseDevice_NET();
                    camera.MV_CC_DestroyDevice_NET();
                    continue;
                }

                _logger.Log($"Захват изображений запущен для камеры {cameraName}.");
                _cameras.Add(camera);
            }

            if (_cameras.Count > 0)
            {
                _logger.Log($"Успешно подключено {_cameras.Count} камер(ы) через SDK.");
            }
            else
            {
                _logger.Log("Не удалось подключить ни одной камеры.");
            }
        }

        /// <summary>
        /// Освобождение ресурсов камер.
        /// </summary>
        public void Cleanup()
        {
            if (_cameras.Count == 0) return;

            _logger.Log("Освобождение ресурсов камер...");
            foreach (var camera in _cameras)
            {
                camera.MV_CC_StopGrabbing_NET();
                camera.MV_CC_CloseDevice_NET();
                camera.MV_CC_DestroyDevice_NET();
            }
            _cameras.Clear();
            _logger.Log("Все камеры отключены и ресурсы освобождены.");
        }

        private string GetCameraName(MyCamera.MV_CC_DEVICE_INFO stDevInfo, int index)
        {
            string cameraName = "Безымянная камера";
            try
            {
                if (stDevInfo.nTLayerType == MyCamera.MV_GIGE_DEVICE)
                {
                    byte[] gigeInfoBytes = stDevInfo.SpecialInfo.stGigEInfo;
                    MyCamera.MV_GIGE_DEVICE_INFO gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(gigeInfoBytes, typeof(MyCamera.MV_GIGE_DEVICE_INFO));
                    cameraName = gigeInfo.chUserDefinedName;
                }
                else if (stDevInfo.nTLayerType == MyCamera.MV_USB_DEVICE)
                {
                    byte[] usbInfoBytes = stDevInfo.SpecialInfo.stUsb3VInfo;
                    MyCamera.MV_USB3_DEVICE_INFO usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(usbInfoBytes, typeof(MyCamera.MV_USB3_DEVICE_INFO));
                    cameraName = usbInfo.chUserDefinedName;
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"Не удалось прочитать имя камеры {index + 1}: {ex.Message}");
            }

            if (string.IsNullOrEmpty(cameraName))
            {
                cameraName = $"Камера {index + 1} (без имени)";
            }
            return cameraName;
        }
    }
}
