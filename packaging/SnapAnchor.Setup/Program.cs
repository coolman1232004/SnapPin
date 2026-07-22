using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SnapAnchor.Setup;

internal readonly record struct InstallProgress(int Percentage, string Message);

internal static class Program
{
    internal const string AppName = "SnapAnchor";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SnapAnchor";
    private static readonly string LegacyAppName = string.Concat("Snap", "Pin");
    private static readonly string LegacyUninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + LegacyAppName;
    private const int MoveFileDelayUntilReboot = 0x4;

    internal static readonly string DefaultInstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
    internal static readonly string DesktopShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");
    internal static readonly string StartMenuDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName);
    internal static readonly string StartMenuShortcut = Path.Combine(StartMenuDirectory, $"{AppName}.lnk");

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
            {
                BeginUninstall();
                return;
            }

            if (args.Length >= 4 && args[0].Equals("--finish-uninstall", StringComparison.OrdinalIgnoreCase))
            {
                FinishUninstall(int.Parse(args[1]), args[2], args[3]);
                return;
            }

            if (args.Length >= 2 && args[0].Equals("--render-previews", StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(args[1]);
                using var preview = new SetupWizardForm(SuggestedInstallDirectory()) { ShowInTaskbar = false };
                preview.Show();
                Application.DoEvents();
                preview.RenderPreviews(args[1]);
                preview.Close();
                return;
            }

            Application.Run(new SetupWizardForm(SuggestedInstallDirectory()));
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, $"{AppName} Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    internal static string SuggestedInstallDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        if (key?.GetValue("InstallLocation") as string is { Length: > 0 } registered)
            return registered;
        using var legacyKey = Registry.CurrentUser.OpenSubKey(LegacyUninstallKey);
        return legacyKey?.GetValue("InstallLocation") as string is { Length: > 0 } legacyRegistered
            ? legacyRegistered
            : DefaultInstallDirectory;
    }

    internal static async Task InstallAsync(
        string requestedDirectory,
        bool createDesktopShortcut,
        bool createStartMenuShortcut,
        IProgress<InstallProgress> progress,
        CancellationToken cancellationToken)
    {
        var installDirectory = ValidateInstallDirectory(requestedDirectory);
        await Task.Run(() =>
        {
            progress.Report(new InstallProgress(2, "Preparing installation..."));
            StopInstalledApp(installDirectory);
            var stagingDirectory = Path.Combine(Path.GetTempPath(), $"SnapAnchor-Install-{Guid.NewGuid():N}");
            var rollbackDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SnapAnchor", "InstallRollback", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            try
            {
                Directory.CreateDirectory(stagingDirectory);
                using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("SnapAnchor.Payload.zip")
                    ?? throw new InvalidOperationException("The installer payload is missing.");
                using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
                var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
                for (var index = 0; index < files.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = files[index];
                    var destination = SafeDestination(stagingDirectory, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                    var percentage = 5 + (int)Math.Round((index + 1) * 65d / Math.Max(1, files.Length));
                    progress.Report(new InstallProgress(percentage, $"Verifying {entry.Name}"));
                }
                if (!File.Exists(Path.Combine(stagingDirectory, "SnapAnchor.exe")))
                    throw new InvalidDataException("The installer payload did not contain SnapAnchor.exe.");

                if (Directory.Exists(installDirectory) && Directory.EnumerateFileSystemEntries(installDirectory).Any())
                {
                    progress.Report(new InstallProgress(73, "Creating rollback copy..."));
                    CopyDirectory(installDirectory, rollbackDirectory, overwrite: true);
                    File.WriteAllText(Path.Combine(rollbackDirectory, "rollback.txt"),
                        $"SnapAnchor rollback created {DateTime.UtcNow:O}{Environment.NewLine}Restore target: {installDirectory}");
                }

                progress.Report(new InstallProgress(78, "Activating verified files..."));
                Directory.CreateDirectory(installDirectory);
                CopyDirectory(stagingDirectory, installDirectory, overwrite: true);
                RemoveLegacyProductFiles(installDirectory);

                progress.Report(new InstallProgress(88, "Installing maintenance tools..."));
                var installedSetup = Path.Combine(installDirectory, "SnapAnchor.Setup.exe");
                var currentSetup = Path.GetFullPath(Environment.ProcessPath!);
                if (!currentSetup.Equals(Path.GetFullPath(installedSetup), StringComparison.OrdinalIgnoreCase))
                    File.Copy(currentSetup, installedSetup, overwrite: true);

                progress.Report(new InstallProgress(92, "Creating shortcuts..."));
                if (createDesktopShortcut)
                    CreateShortcut(DesktopShortcut, Path.Combine(installDirectory, "SnapAnchor.exe"), installDirectory);
                else
                    DeleteIfPresent(DesktopShortcut);

                if (createStartMenuShortcut)
                {
                    Directory.CreateDirectory(StartMenuDirectory);
                    CreateShortcut(StartMenuShortcut, Path.Combine(installDirectory, "SnapAnchor.exe"), installDirectory);
                }
                else
                {
                    DeleteIfPresent(StartMenuShortcut);
                    DeleteEmptyStartMenuDirectory();
                }

                progress.Report(new InstallProgress(97, "Registering SnapAnchor..."));
                RegisterUninstaller(installedSetup, installDirectory,
                    Directory.Exists(rollbackDirectory) ? rollbackDirectory : string.Empty);
                RemoveLegacyRegistration();
                progress.Report(new InstallProgress(100, "Installation completed. A rollback copy was retained when upgrading."));
            }
            catch
            {
                if (Directory.Exists(rollbackDirectory))
                {
                    Directory.CreateDirectory(installDirectory);
                    CopyDirectory(rollbackDirectory, installDirectory, overwrite: true, skipRollbackMarker: true);
                }
                throw;
            }
            finally
            {
                try { if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true); } catch { }
            }
        }, cancellationToken);
    }

    internal static void LaunchInstalledApp(string installDirectory)
    {
        var executable = Path.Combine(installDirectory, "SnapAnchor.exe");
        if (!File.Exists(executable)) return;
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = installDirectory });
    }

    internal static void OpenReadme(string installDirectory)
    {
        var readme = Path.Combine(installDirectory, "README.md");
        if (!File.Exists(readme)) return;
        Process.Start(new ProcessStartInfo(readme) { UseShellExecute = true, WorkingDirectory = installDirectory });
    }

    private static string ValidateInstallDirectory(string requestedDirectory)
    {
        if (string.IsNullOrWhiteSpace(requestedDirectory))
            throw new InvalidOperationException("Choose an installation folder.");
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(requestedDirectory.Trim()));
        var root = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SnapAnchor cannot be installed directly into a drive root.");
        return fullPath;
    }

    private static string SafeDestination(string installDirectory, string archivePath)
    {
        var root = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(root, archivePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The installer payload contains an unsafe path.");
        return destination;
    }

    private static void BeginUninstall()
    {
        var answer = MessageBox.Show(
            $"Remove {AppName}, its shortcuts, and its installed program files?\n\nPersonal settings and capture history are kept.",
            $"Uninstall {AppName}", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        var installDirectory = Path.GetDirectoryName(Path.GetFullPath(Environment.ProcessPath!))
            ?? throw new InvalidOperationException("The installed location could not be determined.");
        StopInstalledApp(installDirectory);
        var temporaryUninstaller = Path.Combine(Path.GetTempPath(), $"SnapAnchor-Uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(Environment.ProcessPath!, temporaryUninstaller, overwrite: true);
        Process.Start(new ProcessStartInfo(temporaryUninstaller)
        {
            UseShellExecute = true,
            Arguments = $"--finish-uninstall {Environment.ProcessId} \"{installDirectory}\" \"{temporaryUninstaller}\""
        });
    }

    private static void FinishUninstall(int parentProcessId, string requestedDirectory, string temporaryUninstaller)
    {
        try { Process.GetProcessById(parentProcessId).WaitForExit(15_000); }
        catch { }

        var requested = Path.GetFullPath(requestedDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var registered = SuggestedInstallDirectory();
        if (!requested.Equals(Path.GetFullPath(registered).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The uninstall directory did not pass its safety check.");

        DeleteIfPresent(DesktopShortcut);
        DeleteIfPresent(StartMenuShortcut);
        DeleteEmptyStartMenuDirectory();
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
        if (Directory.Exists(requested)) Directory.Delete(requested, recursive: true);

        MessageBox.Show($"{AppName} was removed.", $"Uninstall {AppName}", MessageBoxButtons.OK, MessageBoxIcon.Information);
        MoveFileEx(temporaryUninstaller, null, MoveFileDelayUntilReboot);
    }

    private static void StopInstalledApp(string installDirectory)
    {
        var expectedExecutable = Path.GetFullPath(Path.Combine(installDirectory, "SnapAnchor.exe"));
        var legacyExecutable = Path.GetFullPath(Path.Combine(installDirectory, LegacyAppName + ".exe"));
        foreach (var processName in new[] { "SnapAnchor", LegacyAppName })
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                var processPath = Path.GetFullPath(process.MainModule?.FileName ?? "");
                if (!processPath.Equals(expectedExecutable, StringComparison.OrdinalIgnoreCase) &&
                    !processPath.Equals(legacyExecutable, StringComparison.OrdinalIgnoreCase)) continue;
                process.CloseMainWindow();
                if (!process.WaitForExit(2_000)) process.Kill(entireProcessTree: true);
            }
            catch { }
        }
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = "Capture and pin visual references";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void RegisterUninstaller(string installedSetup, string installDirectory, string rollbackDirectory)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true)
            ?? throw new InvalidOperationException("Could not register the uninstaller.");
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.0");
        key.SetValue("Publisher", "SnapAnchor");
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", Path.Combine(installDirectory, "SnapAnchor.exe"));
        key.SetValue("UninstallString", $"\"{installedSetup}\" --uninstall");
        key.SetValue("RollbackLocation", rollbackDirectory);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void RemoveLegacyProductFiles(string installDirectory)
    {
        foreach (var suffix in new[] { ".exe", ".dll", ".deps.json", ".runtimeconfig.json" })
            DeleteIfPresent(Path.Combine(installDirectory, LegacyAppName + suffix));
        DeleteIfPresent(Path.Combine(installDirectory, LegacyAppName + ".Setup.exe"));
        DeleteIfPresent(Path.Combine(installDirectory, "." + LegacyAppName.ToLowerInvariant() + "-package.json"));
        foreach (var culture in new[] { "zh-Hans", "zh-Hant" })
            DeleteIfPresent(Path.Combine(installDirectory, culture, LegacyAppName + ".resources.dll"));
    }

    private static void RemoveLegacyRegistration()
    {
        Registry.CurrentUser.DeleteSubKeyTree(LegacyUninstallKey, throwOnMissingSubKey: false);
        DeleteIfPresent(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), LegacyAppName + ".lnk"));
        var legacyStartMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), LegacyAppName);
        DeleteIfPresent(Path.Combine(legacyStartMenu, LegacyAppName + ".lnk"));
        if (Directory.Exists(legacyStartMenu) && !Directory.EnumerateFileSystemEntries(legacyStartMenu).Any())
            Directory.Delete(legacyStartMenu);
    }

    private static void CopyDirectory(string source, string destination, bool overwrite, bool skipRollbackMarker = false)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            if (skipRollbackMarker && Path.GetFileName(file).Equals("rollback.txt", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite);
        }
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), overwrite, skipRollbackMarker);
    }

    private static void DeleteEmptyStartMenuDirectory()
    {
        if (Directory.Exists(StartMenuDirectory) && !Directory.EnumerateFileSystemEntries(StartMenuDirectory).Any())
            Directory.Delete(StartMenuDirectory);
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, int flags);
}
