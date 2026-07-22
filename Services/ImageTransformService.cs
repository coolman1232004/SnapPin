using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.CompilerServices;

namespace SnapAnchor.Services;

internal static class ImageTransformService
{
    private static readonly ConditionalWeakTable<BitmapSource, TransformCache> Cache = new();

    public static BitmapSource Apply(BitmapSource source, bool grayscale, bool inverted)
    {
        if (!grayscale && !inverted) return source;
        var key = (grayscale ? 1 : 0) | (inverted ? 2 : 0);
        var sourceCache = Cache.GetOrCreateValue(source);
        lock (sourceCache)
        {
            if (sourceCache.Images.TryGetValue(key, out var cached) && cached.TryGetTarget(out var existing))
                return existing;
        }
        var formatted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = formatted.PixelWidth * 4;
        var pixels = new byte[stride * formatted.PixelHeight];
        formatted.CopyPixels(pixels, stride, 0);

        for (var index = 0; index < pixels.Length; index += 4)
        {
            var blue = pixels[index];
            var green = pixels[index + 1];
            var red = pixels[index + 2];
            if (grayscale)
            {
                var gray = (byte)Math.Clamp((int)Math.Round(red * 0.2126 + green * 0.7152 + blue * 0.0722), 0, 255);
                red = green = blue = gray;
            }
            if (inverted)
            {
                red = (byte)(255 - red);
                green = (byte)(255 - green);
                blue = (byte)(255 - blue);
            }
            pixels[index] = blue;
            pixels[index + 1] = green;
            pixels[index + 2] = red;
        }

        var result = BitmapSource.Create(formatted.PixelWidth, formatted.PixelHeight,
            formatted.DpiX, formatted.DpiY, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        lock (sourceCache) sourceCache.Images[key] = new WeakReference<BitmapSource>(result);
        return result;
    }

    private sealed class TransformCache
    {
        internal Dictionary<int, WeakReference<BitmapSource>> Images { get; } = [];
    }
}
