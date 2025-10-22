using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace HikrobotScanner.Services
{
    /// <summary>
    /// Сервис, отвечающий за все операции со штрих-кодами:
    /// генерация, печать, управление счетчиком.
    /// </summary>
    public class BarcodeService
    {
        private const string CounterFileName = "barcode_counter.txt";
        private const string BarcodePrefix = "004466005944";
        private const string BarcodeSuffix = "9";

        private readonly Action<string> _logCallback;
        private readonly Action<string, string> _showErrorCallback;

        public BarcodeService(Action<string> logCallback, Action<string, string> showErrorCallback)
        {
            _logCallback = logCallback;
            _showErrorCallback = showErrorCallback;
        }

        public long LoadCounter()
        {
            try
            {
                if (!File.Exists(CounterFileName)) return 1;
                var content = File.ReadAllText(CounterFileName);
                if (long.TryParse(content, out var savedCounter))
                {
                    return savedCounter;
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка загрузки счетчика: {ex.Message}");
            }
            return 1; // Возвращаем 1 по умолчанию
        }

        public void SaveCounter(long counter)
        {
            try
            {
                File.WriteAllText(CounterFileName, counter.ToString());
            }
            catch (Exception ex)
            {
                Log($"Ошибка сохранения счетчика: {ex.Message}");
            }
        }

        /// <summary>
        /// Генерирует и отправляет на печать штрих-коды.
        /// </summary>
        /// <returns>Возвращает (bool success, long nextCounter)</returns>
        public (bool, long) GenerateAndPrintBarcodes(long startCounter, int quantity)
        {
            var barcodesToPrint = new List<string>();
            long currentCounter = startCounter;

            for (var i = 0; i < quantity; i++)
            {
                var barcode = $"{BarcodePrefix}{currentCounter:D7}{BarcodeSuffix}";
                barcodesToPrint.Add(barcode);
                currentCounter++;
            }

            Log($"Сгенерировано {barcodesToPrint.Count} кодов. Следующий код начнется с номера {currentCounter}.");

            bool printSuccess = PrintBarcodes(barcodesToPrint);

            return (printSuccess, currentCounter);
        }

        private bool PrintBarcodes(List<string> barcodes)
        {
            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() != true)
            {
                Log("Печать отменена пользователем.");
                return false;
            }

            var doc = new FlowDocument
            {
                PageWidth = 2.5 * 96,
                PageHeight = 1.5 * 96,
                PagePadding = new Thickness(5),
                ColumnWidth = 2.5 * 96
            };
            var barcodeWriter = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions { Height = 80, Width = 300, Margin = 10 }
            };

            foreach (var barcodeValue in barcodes)
            {
                var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 20, 0, 5) };
                var pixelData = barcodeWriter.Write(barcodeValue);
                var wpfBitmap = PixelDataToWriteableBitmap(pixelData);

                panel.Children.Add(new Image { HorizontalAlignment = HorizontalAlignment.Center, Source = wpfBitmap, Stretch = Stretch.None });
                panel.Children.Add(new TextBlock { Text = barcodeValue, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 });
                doc.Blocks.Add(new BlockUIContainer(panel));
            }

            try
            {
                Log("Отправка документа на печать...");
                printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"Печать {barcodes.Count} штрих-кодов");
                Log("Документ отправлен на принтер.");
                return true;
            }
            catch (Exception ex)
            {
                Log($"Ошибка печати: {ex.Message}");
                _showErrorCallback($"Не удалось отправить документ на печать. Ошибка: {ex.Message}", "Ошибка печати");
                return false;
            }
        }

        private WriteableBitmap PixelDataToWriteableBitmap(PixelData pixelData)
        {
            var wpfBitmap = new WriteableBitmap(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgr32, null);
            wpfBitmap.WritePixels(new Int32Rect(0, 0, pixelData.Width, pixelData.Height), pixelData.Pixels, pixelData.Width * 4, 0);
            return wpfBitmap;
        }

        private void Log(string message)
        {
            _logCallback?.Invoke(message);
        }
    }
}
