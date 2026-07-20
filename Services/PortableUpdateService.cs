using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace SnapPin.Services;

internal sealed record PortableUpdateRequest(
    int ParentProcessId,
    string SourceDirectory,
    string TargetDirectory,
    string BackupDirectory,
    string ExpectedVersion,
    string PreviousVersion)
{
    internal const string Command = "--portable-update";

    internal static bool IsUpdateCommand(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && arguments[0].Equals(Command, StringComparison.OrdinalIgnoreCase);

    internal static bool TryParse(IReadOnlyList<string> arguments, out PortableUpdateRequest request, out string error)
    {
        request = null!;
        error = string.Empty;
        if (!IsUpdateCommand(arguments)) return false;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index + 1 < arguments.Count; index += 2)
            values[arguments[index]] = arguments[index + 1];
        if (!values.TryGetValue("--parent-pid", out var parentText) || !int.TryParse(parentText, out var parentPid) ||
            !values.TryGetValue("--source", out var source) || !values.TryGetValue("--target", out var target) ||
            !values.TryGetValue("--backup", out var backup) || !values.TryGetValue("--expected-version", out var expected) ||
            !values.TryGetValue("--previous-version", out var previous))
        {
            error = LocalizationService.Current("The portable update request is incomplete.");
            return false;
        }
        try
        {
            source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
            target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
            backup = Path.GetFullPath(backup).TrimEnd(Path.DirectorySeparatorChar);
            var sourceExe = Path.Combine(source, "SnapPin.exe");
            var driveRoot = Path.GetPathRoot(target)?.TrimEnd(Path.DirectorySeparatorChar);
            if (!File.Exists(sourceExe) || target.Equals(driveRoot, StringComparison.OrdinalIgnoreCase) ||
                source.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                error = LocalizationService.Current("The portable update request is not safe to apply.");
                return false;
            }
            request = new PortableUpdateRequest(parentPid, source, target, backup, expected, previous);
            return true;
        }
        catch (Exception ex)
        {
            error = LocalizationService.Format("The portable update request is invalid: {0}", ex.Message);
            return false;
        }
    }
}

internal sealed record PortableUpdateProgress(string Status, double Fraction);
internal sealed record PortableUpdateResult(bool Success, string Message, string LogPath, string ResultPath);
internal sealed record UpdateStartupNotice(bool Success, string PreviousVersion, string CurrentVersion, string Message, string LogPath);

internal static class PortableUpdateService
{
    private const string PackageManifestName = ".snappin-package.json";
    private const string SuccessArgument = "--updated-from";
    private const string FailureArgument = "--update-failed";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

    internal static async Task<PortableUpdateResult> ApplyAsync(PortableUpdateRequest request,
        IProgress<PortableUpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(UpdateService.UpdateRoot());
        Directory.CreateDirectory(request.BackupDirectory);
        var logPath = Path.Combine(UpdateService.UpdateRoot(), "portable-update.log");
        var resultPath = Path.Combine(UpdateService.UpdateRoot(), "last-update-result.json");
        var backupFiles = Path.Combine(request.BackupDirectory, "Files");
        var createdFiles = new List<string>();
        var backedUpFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            Log(logPath, $"Preparing portable update {request.PreviousVersion} -> {request.ExpectedVersion}.");
            progress?.Report(new PortableUpdateProgress(LocalizationService.Current("Waiting for SnapPin to close..."), 0.03));
            await WaitForParentAsync(request.ParentProcessId, cancellationToken);
            ValidateRequest(request);

            var sourceFiles = LoadManagedFiles(request.SourceDirectory, requireExisting: true);
            var previousFiles = LoadManagedFiles(request.TargetDirectory, requireExisting: false);
            var newSet = sourceFiles.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var obsolete = previousFiles.Where(path => !newSet.Contains(path)).ToList();
            EnsureFreeSpace(request, sourceFiles);

            progress?.Report(new PortableUpdateProgress(LocalizationService.Current("Creating rollback backup..."), 0.1));
            Directory.CreateDirectory(backupFiles);
            foreach (var relative in sourceFiles.Concat(obsolete).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = SafeCombine(request.TargetDirectory, relative);
                if (File.Exists(target))
                {
                    var backup = SafeCombine(backupFiles, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup, true);
                    backedUpFiles.Add(relative);
                }
                else if (newSet.Contains(relative))
                {
                    createdFiles.Add(relative);
                }
            }
            AtomicFileService.WriteJson(Path.Combine(request.BackupDirectory, "rollback.json"),
                new RollbackManifest(request.ExpectedVersion, request.TargetDirectory, backedUpFiles.ToList(), createdFiles, DateTime.UtcNow));

            for (var index = 0; index < sourceFiles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = sourceFiles[index];
                var source = SafeCombine(request.SourceDirectory, relative);
                var target = SafeCombine(request.TargetDirectory, relative);
                CopyAtomically(source, target);
                var fraction = 0.16 + (index + 1d) / Math.Max(1, sourceFiles.Count) * 0.7;
                progress?.Report(new PortableUpdateProgress(LocalizationService.Current("Installing updated files..."), fraction));
            }
            foreach (var relative in obsolete)
            {
                var target = SafeCombine(request.TargetDirectory, relative);
                if (File.Exists(target)) File.Delete(target);
            }

            progress?.Report(new PortableUpdateProgress(LocalizationService.Current("Verifying the updated version..."), 0.92));
            var targetExe = Path.Combine(request.TargetDirectory, "SnapPin.exe");
            var actualVersion = FileVersionInfo.GetVersionInfo(targetExe).FileVersion ?? string.Empty;
            if (!actualVersion.StartsWith(request.ExpectedVersion + ".", StringComparison.Ordinal))
                throw new InvalidDataException($"Updated version {actualVersion} did not match {request.ExpectedVersion}.");

            var successMessage = LocalizationService.Format("SnapPin was updated from {0} to {1}.", request.PreviousVersion, request.ExpectedVersion);
            Log(logPath, successMessage + $" Rollback: {request.BackupDirectory}");
            var result = new UpdateOutcome(true, request.PreviousVersion, request.ExpectedVersion, successMessage, logPath, request.BackupDirectory);
            AtomicFileService.WriteJson(resultPath, result);
            progress?.Report(new PortableUpdateProgress(LocalizationService.Current("Update completed. Restarting SnapPin..."), 1));
            return new PortableUpdateResult(true, successMessage, logPath, resultPath);
        }
        catch (Exception ex)
        {
            Log(logPath, "Portable update failed: " + ex);
            progress?.Report(new PortableUpdateProgress(LocalizationService.Current("Update failed. Restoring the previous version..."), 0.94));
            var rollbackError = RestoreRollback(request, backupFiles, backedUpFiles, createdFiles, logPath);
            var message = rollbackError is null ? ex.Message : ex.Message + Environment.NewLine + rollbackError.Message;
            var result = new UpdateOutcome(false, request.PreviousVersion, request.ExpectedVersion, message, logPath, request.BackupDirectory);
            AtomicFileService.WriteJson(resultPath, result);
            progress?.Report(new PortableUpdateProgress(LocalizationService.Current("The previous version was restored. Reopening SnapPin..."), 1));
            return new PortableUpdateResult(false, message, logPath, resultPath);
        }
    }

    internal static Process? RestartTarget(PortableUpdateRequest request, PortableUpdateResult result)
    {
        var executable = Path.Combine(request.TargetDirectory, "SnapPin.exe");
        if (!File.Exists(executable)) return null;
        var info = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = request.TargetDirectory };
        info.ArgumentList.Add(result.Success ? SuccessArgument : FailureArgument);
        info.ArgumentList.Add(result.Success ? request.PreviousVersion : result.ResultPath);
        return Process.Start(info);
    }

    internal static UpdateStartupNotice? ExtractStartupNotice(IReadOnlyList<string> arguments, out string[] remaining)
    {
        var output = new List<string>();
        UpdateStartupNotice? notice = null;
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index].Equals(SuccessArgument, StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Count)
            {
                var previous = arguments[++index];
                var current = typeof(PortableUpdateService).Assembly.GetName().Version?.ToString(3) ?? string.Empty;
                notice = new UpdateStartupNotice(true, previous, current,
                    LocalizationService.Format("SnapPin was updated from {0} to {1}.", previous, current), string.Empty);
                continue;
            }
            if (arguments[index].Equals(FailureArgument, StringComparison.OrdinalIgnoreCase) && index + 1 < arguments.Count)
            {
                var path = arguments[++index];
                try
                {
                    var result = JsonSerializer.Deserialize<UpdateOutcome>(File.ReadAllText(path), JsonOptions);
                    if (result is not null)
                        notice = new UpdateStartupNotice(false, result.PreviousVersion, result.TargetVersion, result.Message, result.LogPath);
                }
                catch (Exception ex)
                {
                    notice = new UpdateStartupNotice(false, string.Empty, string.Empty, ex.Message, string.Empty);
                }
                continue;
            }
            output.Add(arguments[index]);
        }
        remaining = output.ToArray();
        return notice;
    }

    private static async Task WaitForParentAsync(int processId, CancellationToken cancellationToken)
    {
        try
        {
            using var parent = Process.GetProcessById(processId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await parent.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException) { }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("SnapPin did not close within 60 seconds.");
        }
    }

    private static void ValidateRequest(PortableUpdateRequest request)
    {
        var runningDirectory = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!runningDirectory.Equals(request.SourceDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationService.Current("The update helper is not running from the verified staging folder."));
        if (!File.Exists(Path.Combine(request.SourceDirectory, "SnapPin.exe")))
            throw new FileNotFoundException(LocalizationService.Current("The staged SnapPin executable is missing."));
        Directory.CreateDirectory(request.TargetDirectory);
        var probe = Path.Combine(request.TargetDirectory, $".snappin-update-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllText(probe, string.Empty); }
        catch (Exception ex) { throw new UnauthorizedAccessException(LocalizationService.Current("The portable SnapPin folder is not writable."), ex); }
        finally { try { File.Delete(probe); } catch { } }
    }

    private static void EnsureFreeSpace(PortableUpdateRequest request, IEnumerable<string> sourceFiles)
    {
        var required = sourceFiles.Sum(relative => new FileInfo(SafeCombine(request.SourceDirectory, relative)).Length) * 2;
        var root = Path.GetPathRoot(request.TargetDirectory);
        if (!string.IsNullOrWhiteSpace(root) && new DriveInfo(root).AvailableFreeSpace < required)
            throw new IOException(LocalizationService.Format("The portable update needs at least {0} of free space.", UpdateService.FormatBytes(required)));
    }

    private static List<string> LoadManagedFiles(string root, bool requireExisting)
    {
        var manifestPath = Path.Combine(root, PackageManifestName);
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(manifestPath), JsonOptions);
            if (manifest?.Files is { Count: > 0 })
                return manifest.Files.Select(NormalizeRelative).Where(relative => !requireExisting || File.Exists(SafeCombine(root, relative)))
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        if (!requireExisting || !Directory.Exists(root)) return [];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => NormalizeRelative(Path.GetRelativePath(root, path))).ToList();
    }

    private static string NormalizeRelative(string relative)
    {
        relative = relative.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar).Contains(".."))
            throw new InvalidDataException("The package manifest contains an unsafe path.");
        return relative;
    }

    private static string SafeCombine(string root, string relative)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(normalizedRoot, NormalizeRelative(relative)));
        if (!path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The package path escaped its root.");
        return path;
    }

    private static void CopyAtomically(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".snappin-{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(source, temporary, true);
            File.Move(temporary, destination, true);
        }
        finally { try { File.Delete(temporary); } catch { } }
    }

    private static Exception? RestoreRollback(PortableUpdateRequest request, string backupRoot,
        IEnumerable<string> backedUpFiles, IEnumerable<string> createdFiles, string logPath)
    {
        try
        {
            foreach (var relative in createdFiles)
            {
                var target = SafeCombine(request.TargetDirectory, relative);
                if (File.Exists(target)) File.Delete(target);
            }
            foreach (var relative in backedUpFiles)
            {
                var backup = SafeCombine(backupRoot, relative);
                var target = SafeCombine(request.TargetDirectory, relative);
                if (File.Exists(backup)) CopyAtomically(backup, target);
            }
            Log(logPath, "Rollback completed.");
            return null;
        }
        catch (Exception ex)
        {
            Log(logPath, "Rollback failed: " + ex);
            return ex;
        }
    }

    private static void Log(string path, string message)
    {
        try { File.AppendAllText(path, $"{DateTime.UtcNow:O}  {message}{Environment.NewLine}"); } catch { }
    }

    private sealed class PackageManifest
    {
        public string Version { get; set; } = string.Empty;
        public List<string> Files { get; set; } = [];
    }

    private sealed record RollbackManifest(string TargetVersion, string TargetDirectory,
        List<string> BackedUpFiles, List<string> CreatedFiles, DateTime CreatedUtc);

    private sealed record UpdateOutcome(bool Success, string PreviousVersion, string TargetVersion,
        string Message, string LogPath, string RollbackDirectory);
}
