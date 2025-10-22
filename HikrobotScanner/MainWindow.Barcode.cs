using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace HikrobotScanner;

/// <summary>
/// Логика для генерации, печати и управления счетчиком штрих-кодов.
/// </summary>
public partial class MainWindow
{
    private long _barcodeCounter = 1;
    private const string CounterFileName = "barcode_counter.txt";

    private const string BarcodePrefix = "004466005944";
    private const string BarcodeSuffix = "9";

    private void PrintBarcodes(List<string> barcodes)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            Log("Печать отменена пользователем.");
            return;
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

        Log("Отправка документа на печать...");
        printDialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"Печать {barcodes.Count} штрих-кодов");
        Log("Документ отправлен на принтер.");
    }

    private WriteableBitmap PixelDataToWriteableBitmap(PixelData pixelData)
    {
        var wpfBitmap = new WriteableBitmap(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgr32, null);
        wpfBitmap.WritePixels(new Int32Rect(0, 0, pixelData.Width, pixelData.Height), pixelData.Pixels, pixelData.Width * 4, 0);
        return wpfBitmap;
    }

    private void LoadBarcodeCounter()
    {
        try
        {
            if (!File.Exists(CounterFileName)) return;
            var content = File.ReadAllText(CounterFileName);
            if (long.TryParse(content, out var savedCounter))
            {
                _barcodeCounter = savedCounter;
                Log($"Счетчик загружен: {_barcodeCounter}");
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка загрузки счетчика: {ex.Message}");
        }
    }

    private void SaveBarcodeCounter()
    {
        try
        {
            File.WriteAllText(CounterFileName, _barcodeCounter.ToString());
        }
        catch (Exception ex)
        {
            Log($"Ошибка сохранения счетчика: {ex.Message}");
        }
    }

    private void UpdateCounterDisplay()
    {
        if (CounterTextBlock != null)
        {
            CounterTextBlock.Text = _barcodeCounter.ToString("D7");
        }
    }
}
