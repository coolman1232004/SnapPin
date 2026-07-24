using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace SnapAnchor.Services;

internal sealed record DetectedScreenRegion(Rect Bounds, string Name, bool IsElement);

/// <summary>
/// Window / UI-element hover detection for capture.
/// Snow Shot–style architecture (independent implementation):
/// 1) Build a geometry cache of top-level windows + UI Automation elements
///    when capture starts (before / while the overlay is shown).
/// 2) On mouse move, only do geometric hit-tests against that cache.
/// Live AutomationElement.FromPoint / WindowFromPoint cannot see through our
/// full-screen overlay, so they must not be used for hover detection.
/// </summary>
internal static class ElementDetectionService
{
    private sealed record CachedRegion(
        Rect Bounds,
        string Name,
        bool IsElement,
        int ZOrder,
        int Depth,
        IntPtr Window);

    private static readonly object Sync = new();
    private static IReadOnlyList<CachedRegion> _cache = Array.Empty<CachedRegion>();
    private static long _cacheTick;
    private static IntPtr _excludedHwnd;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(8);

    public static DetectedScreenRegion? Detect(Point screenPoint, IntPtr excludedWindow)
        => DetectHierarchy(screenPoint, excludedWindow, includeElements: true).LastOrDefault();

    public static IntPtr WindowHandleAt(Point screenPoint, IntPtr excludedWindow)
    {
        EnsureCache(excludedWindow, force: false);
        var hit = HitTest(screenPoint, includeElements: false).FirstOrDefault();
        return hit?.Window ?? IntPtr.Zero;
    }

    /// <summary>
    /// Build / refresh the geometry cache. Call when the capture overlay opens.
    /// </summary>
    public static void RebuildCache(IntPtr excludedOverlayHwnd)
    {
        lock (Sync)
        {
            _excludedHwnd = excludedOverlayHwnd;
            _cache = BuildCache(excludedOverlayHwnd);
            _cacheTick = Environment.TickCount64;
        }
    }

    public static IReadOnlyList<DetectedScreenRegion> DetectHierarchy(
        Point screenPoint,
        IntPtr excludedWindow,
        bool includeElements = true)
    {
        EnsureCache(excludedWindow, force: false);
        return HitTest(screenPoint, includeElements)
            .Select(region => new DetectedScreenRegion(region.Bounds, region.Name, region.IsElement))
            .ToList();
    }

    public static DetectedScreenRegion? TopExternalWindow()
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligibleWindow(hwnd, IntPtr.Zero)) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        if (found == IntPtr.Zero) return null;
        var rect = WindowRect(found);
        return rect.IsEmpty ? null : new DetectedScreenRegion(rect, WindowTitle(found), false);
    }

    private static void EnsureCache(IntPtr excludedWindow, bool force)
    {
        lock (Sync)
        {
            var age = Environment.TickCount64 - _cacheTick;
            if (!force && _cache.Count > 0 && age >= 0 && age < CacheLifetime.TotalMilliseconds &&
                _excludedHwnd == excludedWindow)
                return;
            _excludedHwnd = excludedWindow;
            _cache = BuildCache(excludedWindow);
            _cacheTick = Environment.TickCount64;
        }
    }

    private static List<CachedRegion> BuildCache(IntPtr excludedOverlayHwnd)
    {
        var regions = new List<CachedRegion>(512);
        var zOrder = 0;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsEligibleWindow(hwnd, excludedOverlayHwnd)) return true;
            var rect = WindowRect(hwnd);
            if (rect.IsEmpty || rect.Width < 8 || rect.Height < 8) return true;

            var title = WindowTitle(hwnd);
            regions.Add(new CachedRegion(rect, title, IsElement: false, ZOrder: zOrder, Depth: 0, Window: hwnd));

            // Collect UI elements under this top-level window once. Hover later
            // only does geometry tests — never live FromPoint through the overlay.
            try
            {
                var root = AutomationElement.FromHandle(hwnd);
                CollectElements(root, rect, hwnd, zOrder, depth: 1, regions, remaining: 400);
            }
            catch
            {
                // Some elevated or custom surfaces expose no UI Automation tree.
            }

            zOrder++;
            return true;
        }, IntPtr.Zero);

        return regions;
    }

    private static void CollectElements(
        AutomationElement parent,
        Rect windowBounds,
        IntPtr window,
        int zOrder,
        int depth,
        List<CachedRegion> destination,
        int remaining)
    {
        if (depth > 10 || remaining <= 0) return;
        AutomationElement? child;
        try { child = TreeWalker.ControlViewWalker.GetFirstChild(parent); }
        catch { return; }

        var budget = remaining;
        while (child is not null && budget > 0)
        {
            AutomationElement? next = null;
            try { next = TreeWalker.ControlViewWalker.GetNextSibling(child); } catch { }

            try
            {
                var current = child.Current;
                if (!current.IsOffscreen)
                {
                    var bounds = current.BoundingRectangle;
                    if (!bounds.IsEmpty && bounds.Width >= 4 && bounds.Height >= 4 &&
                        bounds.IntersectsWith(windowBounds))
                    {
                        // Clip to the owner window so hover outlines never spill
                        // across monitors incorrectly.
                        var clipped = Rect.Intersect(bounds, windowBounds);
                        if (!clipped.IsEmpty && clipped.Width >= 3 && clipped.Height >= 3)
                        {
                            var name = current.Name;
                            if (string.IsNullOrWhiteSpace(name))
                                name = ShortControlType(current.ControlType);
                            if (name.Length > 48) name = name[..45] + "...";

                            // Skip near-duplicates of the whole window chrome.
                            if (!NearlyEqual(clipped, windowBounds))
                            {
                                destination.Add(new CachedRegion(
                                    clipped, name, IsElement: true, zOrder, depth, window));
                                budget--;
                            }

                            CollectElements(child, windowBounds, window, zOrder, depth + 1, destination, budget);
                        }
                    }
                }
            }
            catch
            {
                // Provider may disappear mid-walk.
            }

            child = next;
        }
    }

    private static IReadOnlyList<CachedRegion> HitTest(Point screenPoint, bool includeElements)
    {
        IReadOnlyList<CachedRegion> snapshot;
        lock (Sync) snapshot = _cache;
        if (snapshot.Count == 0) return Array.Empty<CachedRegion>();

        // Top-most top-level window under the pointer (lowest ZOrder among windows).
        CachedRegion? topWindow = null;
        foreach (var region in snapshot)
        {
            if (region.IsElement) continue;
            if (!region.Bounds.Contains(screenPoint)) continue;
            if (topWindow is null || region.ZOrder < topWindow.ZOrder)
                topWindow = region;
        }

        if (topWindow is null) return Array.Empty<CachedRegion>();

        var hits = new List<CachedRegion> { topWindow };
        if (!includeElements) return hits;

        foreach (var region in snapshot)
        {
            if (!region.IsElement || region.Window != topWindow.Window) continue;
            if (!region.Bounds.Contains(screenPoint)) continue;
            if (hits.Any(existing => NearlyEqual(existing.Bounds, region.Bounds))) continue;
            hits.Add(region);
        }

        // Window first, then larger containers, then the deepest / smallest control.
        // Default hover index uses the last entry (deepest).
        hits.Sort((left, right) =>
        {
            if (left.IsElement != right.IsElement)
                return left.IsElement ? 1 : -1;
            var areaCompare = (right.Bounds.Width * right.Bounds.Height)
                .CompareTo(left.Bounds.Width * left.Bounds.Height);
            if (areaCompare != 0) return areaCompare;
            return left.Depth.CompareTo(right.Depth);
        });

        return hits;
    }

    private static bool IsEligibleWindow(IntPtr hwnd, IntPtr excludedOverlayHwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == excludedOverlayHwnd) return false;
        if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd)) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == Environment.ProcessId) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect) || rect.Width < 8 || rect.Height < 8)
            return false;

        var className = new StringBuilder(96);
        NativeMethods.GetClassName(hwnd, className, className.Capacity);
        var name = className.ToString();
        if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" or
            "Windows.UI.Core.CoreWindow") // UWP shell host noise
            return false;

        // Cloaked / invisible shell windows (Win10+).
        if (IsCloaked(hwnd)) return false;
        return true;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            // DWMWA_CLOAKED = 14
            if (NativeMethods.DwmGetWindowAttribute(hwnd, 14, out var cloaked, sizeof(int)) == 0)
                return cloaked != 0;
        }
        catch { }
        return false;
    }

    private static Rect WindowRect(IntPtr hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var rect)
            ? new Rect(rect.Left, rect.Top, Math.Max(0, rect.Width), Math.Max(0, rect.Height))
            : Rect.Empty;

    private static string WindowTitle(IntPtr hwnd)
    {
        try
        {
            var length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0) return "Window";
            var buffer = new StringBuilder(Math.Min(length + 1, 128));
            if (NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity) > 0 &&
                !string.IsNullOrWhiteSpace(buffer.ToString()))
            {
                var title = buffer.ToString().Trim();
                return title.Length > 48 ? title[..45] + "..." : title;
            }
        }
        catch { }
        return "Window";
    }

    private static string ShortControlType(ControlType? type)
    {
        if (type is null) return "UI element";
        var name = type.ProgrammaticName;
        if (string.IsNullOrWhiteSpace(name)) return "UI element";
        return name.Replace("ControlType.", string.Empty, StringComparison.Ordinal);
    }

    private static bool NearlyEqual(Rect first, Rect second) =>
        Math.Abs(first.X - second.X) < 2 && Math.Abs(first.Y - second.Y) < 2 &&
        Math.Abs(first.Width - second.Width) < 2 && Math.Abs(first.Height - second.Height) < 2;
}
