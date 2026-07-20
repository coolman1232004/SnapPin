using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace SnapPin.Services;

internal static class DiagnosticsService
{
    private static readonly object Sync = new();
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapPin", "Diagnostics");
    private static readonly string MarkerPath = Path.Combine(Root, "running.lock");
    private static string _logPath = string.Empty;
    private static bool _started;

    internal static string FolderPath => Root;
    internal static bool RecoveredPreviousCrash { get; private set; }
    internal static string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    internal static bool Start()
    {
        lock (Sync)
        {
            if (_started) return RecoveredPreviousCrash;
            Directory.CreateDirectory(Root);
            RecoveredPreviousCrash = File.Exists(MarkerPath);
            _logPath = Path.Combine(Root, $"SnapPin-{DateTime.Now:yyyyMMdd}.log");
            File.WriteAllText(MarkerPath, $"{Environment.ProcessId}|{DateTime.UtcNow:O}");
            _started = true;
            TrimOldLogs();
            Log("startup", $"SnapPin {Version} started; previousCrash={RecoveredPreviousCrash}");
            return RecoveredPreviousCrash;
        }
    }

    internal static void Log(string category, string message, Exception? exception = null)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Root);
                if (string.IsNullOrWhiteSpace(_logPath))
                    _logPath = Path.Combine(Root, $"SnapPin-{DateTime.Now:yyyyMMdd}.log");
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}";
                if (exception is not null) line += Environment.NewLine + exception;
                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics must never become the reason the capture tool fails.
        }
    }

    internal static void MarkCleanExit()
    {
        Log("shutdown", "Clean exit");
        try { if (File.Exists(MarkerPath)) File.Delete(MarkerPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static void OpenFolder()
    {
        Directory.CreateDirectory(Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Root}\"") { UseShellExecute = true });
    }

    internal static string Summary()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"SnapPin version: {Version}");
        builder.AppendLine($"Windows: {RuntimeInformation.OSDescription}");
        builder.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine($"Process elevated: {ElevationService.IsCurrentProcessElevated()}");
        builder.AppendLine($"Recovered previous crash: {RecoveredPreviousCrash}");
        builder.AppendLine(RuntimeCompatibilityService.Describe(RuntimeCompatibilityService.Snapshot()));
        builder.AppendLine($"Diagnostics folder: {Root}");
        return builder.ToString().TrimEnd();
    }

    private static void TrimOldLogs()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "SnapPin-*.log")
                         .OrderByDescending(File.GetLastWriteTimeUtc)
                         .Skip(14))
                File.Delete(file);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
