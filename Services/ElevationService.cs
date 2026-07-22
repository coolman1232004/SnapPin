using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace SnapAnchor.Services;

internal static class ElevationService
{
    internal static bool NeedsElevation(bool requested, bool isElevated) => requested && !isElevated;

    public static bool IsCurrentProcessElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static bool TryRelaunchElevated(IEnumerable<string> arguments, out string? errorMessage)
    {
        errorMessage = null;
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            errorMessage = LocalizationService.Current("SnapAnchor could not determine its executable path.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            errorMessage = LocalizationService.Current("Administrator launch was cancelled. SnapAnchor will continue with normal permissions for this session.");
            return false;
        }
        catch (Exception exception)
        {
            errorMessage = LocalizationService.Format("SnapAnchor could not restart as administrator: {0}", exception.Message);
            return false;
        }
    }
}
