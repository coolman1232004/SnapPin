using SnapAnchor.Models;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tesseract;
using ZXing;
using ZXing.Common;

namespace SnapAnchor.Services;

internal static class RecognitionService
{
    public static Task<RecognitionResult> RecognizeAsync(BitmapSource image, string language, CancellationToken cancellationToken = default)
    {
        var frozen = image.IsFrozen ? image : Freeze(image);
        return Task.Run(() => Recognize(frozen, language, cancellationToken), cancellationToken);
    }

    private static RecognitionResult Recognize(BitmapSource image, string language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<string>();
        var text = string.Empty;
        IReadOnlyList<RecognizedWord> words = Array.Empty<RecognizedWord>();
        var barcodeText = string.Empty;
        var barcodeFormat = string.Empty;

        try
        {
            var dataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
            if (!Directory.Exists(dataPath))
                throw new DirectoryNotFoundException(LocalizationService.Current("OCR language files are missing."));

            var png = ToPngBytes(image);
            using var engine = new TesseractEngine(dataPath, NormalizeLanguage(language), EngineMode.LstmOnly);
            using var pix = Pix.LoadFromMemory(png);
            using (var page = engine.Process(pix, PageSegMode.Auto))
            {
                text = page.GetText()?.Trim() ?? string.Empty;
                words = ExtractWords(page, cancellationToken);
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                using var sparsePage = engine.Process(pix, PageSegMode.SparseText);
                text = sparsePage.GetText()?.Trim() ?? string.Empty;
                words = ExtractWords(sparsePage, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            errors.Add($"OCR: {ex.Message}");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            var stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            var luminance = new RGBLuminanceSource(pixels, converted.PixelWidth, converted.PixelHeight, RGBLuminanceSource.BitmapFormat.BGRA32);
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions { TryHarder = true, TryInverted = true }
            };
            var results = reader.DecodeMultiple(luminance) ?? [];
            if (results.Length > 0)
            {
                barcodeText = string.Join(Environment.NewLine, results.Select(result => result.Text).Distinct());
                barcodeFormat = string.Join(", ", results.Select(result => result.BarcodeFormat.ToString()).Distinct());
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Code scan: {ex.Message}");
        }

        return new RecognitionResult(text, barcodeText, barcodeFormat, string.Join(Environment.NewLine, errors), words);
    }

    private static IReadOnlyList<RecognizedWord> ExtractWords(Page page, CancellationToken cancellationToken)
    {
        var words = new List<RecognizedWord>();
        using var iterator = page.GetIterator();
        iterator.Begin();
        var lineIndex = -1;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lineIndex < 0 || iterator.IsAtBeginningOf(PageIteratorLevel.TextLine)) lineIndex++;
            var value = iterator.GetText(PageIteratorLevel.Word)?.Trim();
            if (string.IsNullOrWhiteSpace(value) ||
                !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds) ||
                bounds.Width <= 0 || bounds.Height <= 0)
                continue;
            words.Add(new RecognizedWord(
                value,
                bounds.X1,
                bounds.Y1,
                bounds.Width,
                bounds.Height,
                Math.Max(0, lineIndex),
                iterator.GetConfidence(PageIteratorLevel.Word)));
        }
        while (iterator.Next(PageIteratorLevel.Word));
        return words;
    }

    private static string NormalizeLanguage(string language)
    {
        var installed = new HashSet<string>(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "tessdata"), "*.traineddata")
            .Select(path => Path.GetFileNameWithoutExtension(path)), StringComparer.OrdinalIgnoreCase);
        var chosen = (language ?? "eng").Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(installed.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return chosen.Count > 0 ? string.Join('+', chosen) : "eng";
    }

    private static byte[] ToPngBytes(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static BitmapSource Freeze(BitmapSource source)
    {
        var clone = source.Clone();
        clone.Freeze();
        return clone;
    }
}
