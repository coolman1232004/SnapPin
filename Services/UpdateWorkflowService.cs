using SnapPin.Windows;
using System.Windows;

namespace SnapPin.Services;

internal static class UpdateWorkflowService
{
    internal static async Task<bool> CheckAndRunAsync(Window owner, string? feedUrl, bool automatic)
    {
        var update = await UpdateService.CheckAsync(feedUrl);
        if (!update.UpdateAvailable || string.IsNullOrWhiteSpace(update.DownloadUrl))
        {
            if (!automatic)
                MessageBox.Show(owner, update.Message, LocalizationService.Current("SnapPin update"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return RunAvailable(owner, update);
    }

    internal static bool RunAvailable(Window owner, UpdateCheckResult update)
    {
        PreparedUpdate? prepared;
        if (UpdateService.TryLoadPending(update, out var pending))
        {
            prepared = pending;
        }
        else
        {
            if (!UpdateAvailableWindow.Ask(owner, update)) return false;
            prepared = UpdateProgressWindow.Run(owner, update);
            if (prepared is null) return false;
        }

        if (UpdateReadyWindow.Ask(owner, prepared) != UpdateReadyDecision.ApplyNow)
        {
            UpdateService.SavePending(prepared);
            return false;
        }
        return UpdateService.LaunchPrepared(prepared);
    }
}
