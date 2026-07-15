using System.Windows;

namespace SnapPin.Services;

internal static class OverlayLayoutService
{
    public static Point PlaceBelowAndKeepVisible(Rect anchor, Size panel, Size viewport, double margin = 8)
    {
        var width = Math.Min(Math.Max(1, panel.Width), Math.Max(1, viewport.Width - margin * 2));
        var height = Math.Min(Math.Max(1, panel.Height), Math.Max(1, viewport.Height - margin * 2));
        var left = Math.Clamp(anchor.Right - width, margin, Math.Max(margin, viewport.Width - width - margin));
        var top = Math.Clamp(anchor.Bottom + margin, margin, Math.Max(margin, viewport.Height - height - margin));
        return new Point(left, top);
    }
}
