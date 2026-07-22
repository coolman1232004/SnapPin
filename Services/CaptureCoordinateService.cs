using System.Windows;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SnapAnchor.Services;

internal static class CaptureCoordinateService
{
    internal static Int32Rect ToBitmapRegion(Point screenTopLeft, Point screenBottomRight, DrawingRectangle virtualBounds, int bitmapWidth, int bitmapHeight)
    {
        var left = Math.Min(screenTopLeft.X, screenBottomRight.X) - virtualBounds.Left;
        var top = Math.Min(screenTopLeft.Y, screenBottomRight.Y) - virtualBounds.Top;
        var right = Math.Max(screenTopLeft.X, screenBottomRight.X) - virtualBounds.Left;
        var bottom = Math.Max(screenTopLeft.Y, screenBottomRight.Y) - virtualBounds.Top;
        var x = Math.Clamp((int)Math.Round(left), 0, Math.Max(0, bitmapWidth - 1));
        var y = Math.Clamp((int)Math.Round(top), 0, Math.Max(0, bitmapHeight - 1));
        var pixelRight = Math.Clamp((int)Math.Round(right), x + 1, bitmapWidth);
        var pixelBottom = Math.Clamp((int)Math.Round(bottom), y + 1, bitmapHeight);
        return new Int32Rect(x, y, pixelRight - x, pixelBottom - y);
    }
}
