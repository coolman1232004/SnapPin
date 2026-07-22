using System.IO;

namespace SnapAnchor.Services;

internal sealed record UpdateCheckScheduleState(DateTimeOffset LastCheckedUtc);

internal static class UpdateCheckScheduleService
{
    private static string StatePath => Path.Combine(UpdateService.UpdateRoot(), "update-check-schedule.json");

    internal static bool IsDailyCheckDue(DateTimeOffset now) =>
        IsDailyCheckDue(LoadLastCheckedUtc(), now, TimeZoneInfo.Local);

    internal static bool IsDailyCheckDue(DateTimeOffset? lastCheckedUtc, DateTimeOffset now, TimeZoneInfo timeZone)
    {
        if (lastCheckedUtc is null) return true;
        var lastLocalDate = TimeZoneInfo.ConvertTime(lastCheckedUtc.Value, timeZone).Date;
        var currentLocalDate = TimeZoneInfo.ConvertTime(now, timeZone).Date;
        return lastLocalDate < currentLocalDate;
    }

    internal static void MarkChecked(DateTimeOffset checkedUtc)
    {
        try
        {
            AtomicFileService.WriteJson(StatePath, new UpdateCheckScheduleState(checkedUtc.ToUniversalTime()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsService.Log("update-schedule", ex.Message, ex);
        }
    }

    private static DateTimeOffset? LoadLastCheckedUtc()
    {
        try
        {
            return AtomicFileService.TryReadJson<UpdateCheckScheduleState>(StatePath, out var state)
                ? state?.LastCheckedUtc
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DiagnosticsService.Log("update-schedule", ex.Message, ex);
            return null;
        }
    }
}
