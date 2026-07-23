using System.Windows.Media;

namespace SnapAnchor.Services;

/// <summary>
/// Chooses a bitmap resampler for pinned screenshots. Snow Shot keeps its
/// source-resolution canvas and lets the GPU linearly sample it at the final
/// physical window size. WPF's equivalent is LowQuality (bilinear), despite
/// the unfortunate enum name. Fant/HighQuality is smoother but visibly softens
/// small captured text.
/// </summary>
internal static class PinScalingQuality
{
    private const double NativePixelTolerance = 0.75;
    internal const int MinimumZoomPercent = 20;
    internal const int MaximumZoomPercent = 300;
    internal const int WheelStepPercent = 10;

    internal static BitmapScalingMode ModeFor(
        double logicalWidth,
        double dpiScaleX,
        int sourcePixelWidth,
        bool animating)
    {
        var physicalWidth = Math.Max(1, logicalWidth) * Math.Max(0.1, dpiScaleX);
        return Math.Abs(physicalWidth - Math.Max(1, sourcePixelWidth)) <= NativePixelTolerance
            ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.LowQuality;
    }

    internal static int ZoomPercentForPhysicalWidth(int physicalWidth, int sourcePixelWidth) =>
        Math.Clamp(
            (int)Math.Round(Math.Max(1, physicalWidth) * 100d / Math.Max(1, sourcePixelWidth)),
            MinimumZoomPercent,
            MaximumZoomPercent);

    internal static int NextWheelZoomPercent(int physicalWidth, int sourcePixelWidth, int wheelDelta)
    {
        var current = ZoomPercentForPhysicalWidth(physicalWidth, sourcePixelWidth);
        var direction = Math.Sign(wheelDelta);
        return Math.Clamp(
            current + direction * WheelStepPercent,
            MinimumZoomPercent,
            MaximumZoomPercent);
    }

    internal static System.Drawing.Size PhysicalSizeForPercent(
        int sourcePixelWidth,
        int sourcePixelHeight,
        int zoomPercent) => new(
            Math.Max(1, (int)Math.Round(Math.Max(1, sourcePixelWidth) * Math.Clamp(zoomPercent, MinimumZoomPercent, MaximumZoomPercent) / 100d)),
            Math.Max(1, (int)Math.Round(Math.Max(1, sourcePixelHeight) * Math.Clamp(zoomPercent, MinimumZoomPercent, MaximumZoomPercent) / 100d)));
}
