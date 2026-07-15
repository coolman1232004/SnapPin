using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapPin.Services;

internal sealed record RunningAppOption(string ProcessName, string DisplayName);

internal static class CaptureExclusionService
{
    public static IReadOnlyList<RunningAppOption> RunningApps()
    {
        var ownProcessId = Environment.ProcessId;
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.Id == ownProcessId || process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.ProcessName)) continue;
                var title = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? process.ProcessName : process.MainWindowTitle.Trim();
                apps.TryAdd(process.ProcessName, $"{title}  ({process.ProcessName}.exe)");
            }
            catch
            {
                // A protected process may disappear or reject inspection while the list is built.
            }
            finally
            {
                process.Dispose();
            }
        }

        return apps
            .OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(pair => new RunningAppOption(pair.Key, pair.Value))
            .ToList();
    }

    public static BitmapSource Apply(BitmapSource source, System.Drawing.Rectangle capturedBounds, AppSettings settings)
    {
        var excluded = new HashSet<string>(settings.CaptureExcludedProcesses, StringComparer.OrdinalIgnoreCase);
        // SnapPin's own windows use WDA_EXCLUDEFROMCAPTURE so Windows can reveal the content behind them.
        // Third-party windows cannot be assigned that flag by SnapPin, so those are covered after capture.
        const bool redactOwnWindows = false;
        if (excluded.Count == 0) return source;

        var windows = FindExcludedWindows(excluded, redactOwnWindows);
        if (windows.Count == 0) return source;

        var scaleX = source.PixelWidth / (double)Math.Max(1, capturedBounds.Width);
        var scaleY = source.PixelHeight / (double)Math.Max(1, capturedBounds.Height);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            foreach (var window in windows)
            {
                var intersection = System.Drawing.Rectangle.Intersect(capturedBounds, window.Bounds);
                if (intersection.Width <= 0 || intersection.Height <= 0) continue;
                var rect = new Rect(
                    (intersection.Left - capturedBounds.Left) * scaleX,
                    (intersection.Top - capturedBounds.Top) * scaleY,
                    intersection.Width * scaleX,
                    intersection.Height * scaleY);
                drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(248, 17, 24, 39)), null, rect);

                if (rect.Width >= 150 && rect.Height >= 52)
                {
                    var label = new FormattedText(
                        $"{window.ProcessName}.exe excluded",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Semibold"),
                        14,
                        Brushes.White,
                        1.0)
                    { MaxTextWidth = Math.Max(1, rect.Width - 28), Trimming = TextTrimming.CharacterEllipsis };
                    drawing.DrawText(label, new Point(rect.Left + 14, rect.Top + 14));
                }
            }
        }

        var result = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private static List<ExcludedWindow> FindExcludedWindows(HashSet<string> excluded, bool redactOwnWindows)
    {
        var result = new List<ExcludedWindow>();
        var ownProcessId = (uint)Environment.ProcessId;
        NativeMethods.EnumWindows((handle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(handle) || NativeMethods.IsIconic(handle) || !NativeMethods.GetWindowRect(handle, out var rect)) return true;
            if (rect.Width <= 1 || rect.Height <= 1) return true;
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            var processName = string.Empty;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch { }
            if ((redactOwnWindows && processId == ownProcessId) || excluded.Contains(processName))
                result.Add(new ExcludedWindow(processName.Length == 0 ? "Application" : processName,
                    new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height)));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private sealed record ExcludedWindow(string ProcessName, System.Drawing.Rectangle Bounds);
}
