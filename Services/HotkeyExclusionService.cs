using System.Diagnostics;

namespace SnapPin.Services;

internal static class HotkeyExclusionService
{
    public static bool IsForegroundExcluded(IReadOnlyCollection<string> excludedProcesses)
    {
        if (excludedProcesses.Count == 0) return false;
        var handle = NativeMethods.GetForegroundWindow();
        if (handle == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(handle, out var processId);
        if (processId == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return IsExcluded(process.ProcessName, excludedProcesses);
        }
        catch
        {
            // Foreground processes can close or reject inspection between calls.
            return false;
        }
    }

    internal static bool IsExcluded(string? processName, IEnumerable<string> excludedProcesses) =>
        !string.IsNullOrWhiteSpace(processName) &&
        excludedProcesses.Contains(processName.Trim(), StringComparer.OrdinalIgnoreCase);

    internal static List<string> Normalize(IEnumerable<string>? processNames) =>
        (processNames ?? [])
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
