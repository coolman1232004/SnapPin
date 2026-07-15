using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace SnapPin.Services;

internal sealed record DetectedScreenRegion(Rect Bounds, string Name, bool IsElement);

internal static class ElementDetectionService
{
    public static DetectedScreenRegion? Detect(Point screenPoint, IntPtr excludedWindow)
        => DetectHierarchy(screenPoint, excludedWindow).LastOrDefault();

    public static IntPtr WindowHandleAt(Point screenPoint, IntPtr excludedWindow)
        => FindWindow(screenPoint, excludedWindow);

    public static IReadOnlyList<DetectedScreenRegion> DetectHierarchy(Point screenPoint, IntPtr excludedWindow)
    {
        var window = FindWindow(screenPoint, excludedWindow);
        if (window == IntPtr.Zero) return [];
        var windowRect = Rectangle(window);
        if (windowRect.IsEmpty) return [];
        var regions = new List<DetectedScreenRegion> { new(windowRect, "Window", false) };

        try
        {
            var root = AutomationElement.FromHandle(window);
            foreach (var element in ElementPath(root, screenPoint, 0))
            {
                Rect bounds;
                string name;
                try
                {
                    bounds = element.Current.BoundingRectangle;
                    name = element.Current.Name;
                }
                catch { continue; }
                if (bounds.IsEmpty || bounds.Width < 4 || bounds.Height < 4 || !bounds.Contains(screenPoint)) continue;
                if (regions.Any(region => NearlyEqual(region.Bounds, bounds))) continue;
                regions.Add(new DetectedScreenRegion(bounds, string.IsNullOrWhiteSpace(name) ? "UI element" : name, true));
            }
        }
        catch
        {
            // Some elevated or custom-rendered windows do not expose UI Automation.
        }

        return regions;
    }

    public static DetectedScreenRegion? TopExternalWindow()
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligible(hwnd, IntPtr.Zero)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) return null;
        var rect = Rectangle(found);
        return rect.IsEmpty ? null : new DetectedScreenRegion(rect, "Active window", false);
    }

    private static IntPtr FindWindow(Point point, IntPtr excludedWindow)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligible(hwnd, excludedWindow)) return true;
            var rect = Rectangle(hwnd);
            if (!rect.Contains(point)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsEligible(IntPtr hwnd, IntPtr excludedWindow)
    {
        if (hwnd == IntPtr.Zero || hwnd == excludedWindow || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == Environment.ProcessId) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width < 20 || rect.Height < 20) return false;
        var className = new StringBuilder(128);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        return className.ToString() is not ("Shell_TrayWnd" or "Progman" or "WorkerW");
    }

    private static Rect Rectangle(IntPtr hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var rect)
            ? new Rect(rect.Left, rect.Top, Math.Max(0, rect.Width), Math.Max(0, rect.Height))
            : Rect.Empty;

    private static IReadOnlyList<AutomationElement> ElementPath(AutomationElement element, Point point, int depth)
    {
        if (depth >= 12) return [];
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            for (var child = walker.GetFirstChild(element); child is not null; child = walker.GetNextSibling(child))
            {
                Rect bounds;
                try { bounds = child.Current.BoundingRectangle; }
                catch { continue; }
                if (bounds.IsEmpty || !bounds.Contains(point)) continue;
                var path = new List<AutomationElement> { child };
                path.AddRange(ElementPath(child, point, depth + 1));
                return path;
            }
        }
        catch
        {
            return [];
        }
        return [];
    }

    private static bool NearlyEqual(Rect first, Rect second) =>
        Math.Abs(first.X - second.X) < 1 && Math.Abs(first.Y - second.Y) < 1 &&
        Math.Abs(first.Width - second.Width) < 1 && Math.Abs(first.Height - second.Height) < 1;
}
