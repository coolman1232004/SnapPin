using System.Windows.Media;

namespace SnapAnchor.Services;

/// <summary>
/// Chooses a bitmap resampler for pinned screenshots. Native-size images are
/// kept pixel exact; resized screenshots use interpolation suited to text.
/// </summary>
internal static class PinScalingQuality
{
    private const double NativePixelTolerance = 0.75;

    internal static BitmapScalingMode ModeFor(
        double logicalWidth,
        double dpiScaleX,
        int sourcePixelWidth,
        bool animating)
    {
        if (animating) return BitmapScalingMode.LowQuality;

        var physicalWidth = Math.Max(1, logicalWidth) * Math.Max(0.1, dpiScaleX);
        return Math.Abs(physicalWidth - Math.Max(1, sourcePixelWidth)) <= NativePixelTolerance
            ? BitmapScalingMode.NearestNeighbor
            : BitmapScalingMode.HighQuality;
    }
}
