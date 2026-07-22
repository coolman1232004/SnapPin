using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace SnapAnchor.Services;

internal sealed record DetectedScreenRegion(Rect Bounds, string Name, bool IsElement);

internal static class ElementDetectionService
{
    private static readonly object CacheSync = new();
    private static ElementSnapshot? _snapshot;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(1.25);

    private sealed record CachedElement(Rect Bounds, string Name, int ParentIndex, int Depth);
    private sealed record ElementSnapshot(IntPtr Window, Rect WindowBounds, DateTime CreatedUtc, IReadOnlyList<CachedElement> Elements);

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
            var snapshot = SnapshotFor(window, windowRect);
            var candidateIndex = snapshot.Elements
                .Select((element, index) => (Element: element, Index: index))
                .Where(item => item.Element.Bounds.Contains(screenPoint))
                .OrderByDescending(item => item.Element.Depth)
                .ThenBy(item => item.Element.Bounds.Width * item.Element.Bounds.Height)
                .Select(item => item.Index)
                .FirstOrDefault(-1);

            if (candidateIndex >= 0)
            {
                var path = new Stack<CachedElement>();
                while (candidateIndex >= 0 && candidateIndex < snapshot.Elements.Count)
                {
                    var element = snapshot.Elements[candidateIndex];
                    path.Push(element);
                    candidateIndex = element.ParentIndex;
                }
                foreach (var element in path)
                {
                    if (!element.Bounds.Contains(screenPoint) || regions.Any(region => NearlyEqual(region.Bounds, element.Bounds))) continue;
                    regions.Add(new DetectedScreenRegion(element.Bounds, element.Name, true));
                }
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

    private static ElementSnapshot SnapshotFor(IntPtr window, Rect windowRect)
    {
        lock (CacheSync)
        {
            if (_snapshot is { } existing && existing.Window == window &&
                DateTime.UtcNow - existing.CreatedUtc <= CacheLifetime && NearlyEqual(existing.WindowBounds, windowRect))
                return existing;
        }

        var elements = new List<CachedElement>();
        try
        {
            var root = AutomationElement.FromHandle(window);
            BuildSnapshot(root, windowRect, -1, 0, elements);
        }
        catch { }

        var created = new ElementSnapshot(window, windowRect, DateTime.UtcNow, elements);
        lock (CacheSync) _snapshot = created;
        return created;
    }

    private static void BuildSnapshot(AutomationElement parent, Rect windowBounds, int parentIndex, int depth,
        List<CachedElement> destination)
    {
        if (depth >= 12 || destination.Count >= 1800) return;
        var walker = TreeWalker.ControlViewWalker;
        AutomationElement? child;
        try { child = walker.GetFirstChild(parent); }
        catch { return; }

        while (child is not null && destination.Count < 1800)
        {
            var next = default(AutomationElement);
            try { next = walker.GetNextSibling(child); } catch { }
            try
            {
                var bounds = child.Current.BoundingRectangle;
                if (!bounds.IsEmpty && bounds.Width >= 4 && bounds.Height >= 4 && bounds.IntersectsWith(windowBounds) &&
                    !child.Current.IsOffscreen)
                {
                    var name = child.Current.Name;
                    var currentIndex = destination.Count;
                    destination.Add(new CachedElement(bounds,
                        string.IsNullOrWhiteSpace(name) ? "UI element" : name, parentIndex, depth + 1));
                    BuildSnapshot(child, windowBounds, currentIndex, depth + 1, destination);
                }
            }
            catch
            {
                // A provider may disappear while its window is changing.
            }
            child = next;
        }
    }

    private static bool NearlyEqual(Rect first, Rect second) =>
        Math.Abs(first.X - second.X) < 1 && Math.Abs(first.Y - second.Y) < 1 &&
        Math.Abs(first.Width - second.Width) < 1 && Math.Abs(first.Height - second.Height) < 1;
}
