using System.Collections.Concurrent;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace SnapAnchor.Services;

internal sealed record DetectedScreenRegion(Rect Bounds, string Name, bool IsElement);

/// <summary>
/// Resolves the window and UI Automation element under the pointer for capture
/// hover outlines. Public Windows APIs only (WindowFromPoint, UI Automation).
/// Behaviour is inspired by common screenshot tools; implementation is independent.
/// </summary>
internal static class ElementDetectionService
{
    private static readonly ConcurrentDictionary<IntPtr, string> WindowTitles = new();
    private static readonly object PointCacheSync = new();
    private static PointCacheEntry? _pointCache;

    private sealed record PointCacheEntry(
        int BucketX,
        int BucketY,
        bool IncludeElements,
        IntPtr Window,
        IReadOnlyList<DetectedScreenRegion> Regions,
        long TickMs);

    public static DetectedScreenRegion? Detect(Point screenPoint, IntPtr excludedWindow)
        => DetectHierarchy(screenPoint, excludedWindow, includeElements: true).LastOrDefault();

    public static IntPtr WindowHandleAt(Point screenPoint, IntPtr excludedWindow)
        => FindWindow(screenPoint, excludedWindow);

    public static IReadOnlyList<DetectedScreenRegion> DetectHierarchy(
        Point screenPoint,
        IntPtr excludedWindow,
        bool includeElements = true)
    {
        // Spatial cache: ignore sub-pixel jitter and re-use recent results so
        // hover stays fluid while the pointer rests on one control.
        var bucketX = (int)Math.Floor(screenPoint.X / 3);
        var bucketY = (int)Math.Floor(screenPoint.Y / 3);
        var now = Environment.TickCount64;
        lock (PointCacheSync)
        {
            if (_pointCache is { } cached &&
                cached.BucketX == bucketX &&
                cached.BucketY == bucketY &&
                cached.IncludeElements == includeElements &&
                now - cached.TickMs < 80)
            {
                return cached.Regions;
            }
        }

        var window = FindWindow(screenPoint, excludedWindow);
        if (window == IntPtr.Zero) return [];
        var windowRect = Rectangle(window);
        if (windowRect.IsEmpty) return [];

        var regions = new List<DetectedScreenRegion>(8)
        {
            new(windowRect, WindowLabel(window), false)
        };

        if (includeElements)
        {
            try
            {
                if (TryElementChainFromPoint(screenPoint, windowRect, out var livePath))
                {
                    foreach (var element in livePath)
                    {
                        if (!element.Bounds.Contains(screenPoint) ||
                            regions.Any(region => NearlyEqual(region.Bounds, element.Bounds)))
                            continue;
                        regions.Add(new DetectedScreenRegion(element.Bounds, element.Name, true));
                    }
                }
            }
            catch
            {
                // Elevated or custom-rendered windows may not expose UI Automation.
            }
        }

        IReadOnlyList<DetectedScreenRegion> result = regions;
        lock (PointCacheSync)
        {
            _pointCache = new PointCacheEntry(bucketX, bucketY, includeElements, window, result, now);
        }
        return result;
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
        return rect.IsEmpty ? null : new DetectedScreenRegion(rect, WindowLabel(found), false);
    }

    private static bool TryElementChainFromPoint(Point screenPoint, Rect windowBounds, out List<(Rect Bounds, string Name)> path)
    {
        path = [];
        try
        {
            // FromPoint is much faster than walking the whole window tree.
            var element = AutomationElement.FromPoint(screenPoint);
            if (element is null) return false;

            var chain = new List<(Rect Bounds, string Name)>(8);
            var current = element;
            for (var depth = 0; current is not null && depth < 10; depth++)
            {
                Rect bounds;
                string name;
                ControlType? controlType;
                try
                {
                    var currentInfo = current.Current;
                    bounds = currentInfo.BoundingRectangle;
                    name = currentInfo.Name;
                    controlType = currentInfo.ControlType;
                }
                catch
                {
                    break;
                }

                if (controlType == ControlType.Window && chain.Count > 0)
                    break;

                if (!bounds.IsEmpty && bounds.Width >= 2 && bounds.Height >= 2 &&
                    bounds.IntersectsWith(windowBounds) &&
                    bounds.Width <= windowBounds.Width + 8 &&
                    bounds.Height <= windowBounds.Height + 8)
                {
                    var label = string.IsNullOrWhiteSpace(name)
                        ? ControlLabel(controlType)
                        : name.Trim();
                    if (label.Length > 48) label = label[..45] + "...";
                    chain.Add((bounds, label));
                }

                try { current = TreeWalker.ControlViewWalker.GetParent(current); }
                catch { break; }
            }

            // Root → leaf (window already added separately).
            chain.Reverse();
            path = chain
                .Where(item => !NearlyEqual(item.Bounds, windowBounds))
                .GroupBy(item =>
                    ((int)item.Bounds.X, (int)item.Bounds.Y, (int)item.Bounds.Width, (int)item.Bounds.Height))
                .Select(group => group.First())
                .ToList();
            return path.Count > 0;
        }
        catch
        {
            path = [];
            return false;
        }
    }

    private static string ControlLabel(ControlType? controlType)
    {
        if (controlType is null) return "UI element";
        var name = controlType.ProgrammaticName;
        if (string.IsNullOrWhiteSpace(name)) return "UI element";
        return name.Replace("ControlType.", string.Empty, StringComparison.Ordinal);
    }

    private static string WindowLabel(IntPtr hwnd)
    {
        if (WindowTitles.TryGetValue(hwnd, out var cached) && cached.Length > 0)
            return cached;

        try
        {
            var length = NativeMethods.GetWindowTextLength(hwnd);
            if (length > 0)
            {
                var buffer = new StringBuilder(Math.Min(length + 1, 128));
                if (NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity) > 0 &&
                    !string.IsNullOrWhiteSpace(buffer.ToString()))
                {
                    var title = buffer.ToString().Trim();
                    if (title.Length > 48) title = title[..45] + "...";
                    WindowTitles[hwnd] = title;
                    return title;
                }
            }
        }
        catch { }

        WindowTitles[hwnd] = "Window";
        return "Window";
    }

    private static IntPtr FindWindow(Point point, IntPtr excludedWindow)
    {
        var native = new NativeMethods.NativePoint
        {
            X = (int)Math.Round(point.X),
            Y = (int)Math.Round(point.Y)
        };
        var hit = NativeMethods.WindowFromPoint(native);
        if (hit != IntPtr.Zero)
        {
            var root = NativeMethods.GetAncestor(hit, NativeMethods.GaRoot);
            if (root == IntPtr.Zero) root = hit;
            if (IsEligible(root, excludedWindow) && Rectangle(root).Contains(point))
                return root;

            // Sometimes WindowFromPoint returns a child of an owned top-level
            // window that is not the GA_ROOT we want; climb owner/parent once.
            var parent = NativeMethods.GetAncestor(hit, 1 /* GA_PARENT */);
            if (parent != IntPtr.Zero && parent != hit)
            {
                var top = NativeMethods.GetAncestor(parent, NativeMethods.GaRoot);
                if (top == IntPtr.Zero) top = parent;
                if (IsEligible(top, excludedWindow) && Rectangle(top).Contains(point))
                    return top;
            }
        }

        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligible(hwnd, excludedWindow)) return true;
            if (!Rectangle(hwnd).Contains(point)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private static bool IsEligible(IntPtr hwnd, IntPtr excludedWindow)
    {
        if (hwnd == IntPtr.Zero || hwnd == excludedWindow || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == Environment.ProcessId) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width < 8 || rect.Height < 8) return false;
        var className = new StringBuilder(96);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        return className.ToString() is not ("Shell_TrayWnd" or "Progman" or "WorkerW" or "Shell_SecondaryTrayWnd");
    }

    private static Rect Rectangle(IntPtr hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var rect)
            ? new Rect(rect.Left, rect.Top, Math.Max(0, rect.Width), Math.Max(0, rect.Height))
            : Rect.Empty;

    private static bool NearlyEqual(Rect first, Rect second) =>
        Math.Abs(first.X - second.X) < 1 && Math.Abs(first.Y - second.Y) < 1 &&
        Math.Abs(first.Width - second.Width) < 1 && Math.Abs(first.Height - second.Height) < 1;
}
