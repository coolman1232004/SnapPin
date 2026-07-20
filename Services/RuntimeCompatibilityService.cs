using Forms = System.Windows.Forms;

namespace SnapPin.Services;

internal sealed record DisplayCompatibilityInfo(
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool Primary,
    bool Portrait);

internal sealed record RuntimeCompatibilityInfo(
    bool RemoteSession,
    IReadOnlyList<DisplayCompatibilityInfo> Displays);

internal static class RuntimeCompatibilityService
{
    internal static RuntimeCompatibilityInfo Snapshot()
    {
        var displays = Forms.Screen.AllScreens
            .Select(screen => new DisplayCompatibilityInfo(
                screen.DeviceName,
                screen.Bounds.Left,
                screen.Bounds.Top,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.Primary,
                screen.Bounds.Height > screen.Bounds.Width))
            .OrderByDescending(display => display.Primary)
            .ThenBy(display => display.Left)
            .ThenBy(display => display.Top)
            .ToList();
        return new RuntimeCompatibilityInfo(Forms.SystemInformation.TerminalServerSession, displays);
    }

    internal static string Describe(RuntimeCompatibilityInfo info)
    {
        var mode = info.RemoteSession ? "remote" : "local";
        var displays = info.Displays.Count == 0
            ? "none"
            : string.Join("; ", info.Displays.Select(display =>
                $"{display.DeviceName} {display.Width}x{display.Height}@{display.Left},{display.Top}" +
                (display.Portrait ? " portrait" : string.Empty) + (display.Primary ? " primary" : string.Empty)));
        return $"Session: {mode}; displays: {displays}";
    }
}
