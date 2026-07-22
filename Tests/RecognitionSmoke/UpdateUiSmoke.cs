using SnapAnchor.Services;
using SnapAnchor.Windows;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapAnchor.RecognitionSmoke;

internal static class UpdateUiSmoke
{
    internal static void Run()
    {
        Program.RunSta(() =>
        {
            LocalizationService.Configure(LocalizationService.English);
            var owner = new Window
            {
                Width = 1,
                Height = 1,
                Left = -10000,
                Top = -10000,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Opacity = 0
            };
            owner.Show();
            var update = new UpdateCheckResult(true, "Available", "https://example.com/update.zip", "9.9.9",
                PackageKind: UpdatePackageKind.Portable, DownloadSize: 85_000_000,
                ReleaseNotes: "Safer portable updates", ReleasePageUrl: "https://example.com/release");

            var progressConstructor = typeof(UpdateProgressWindow)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var window = (UpdateProgressWindow)progressConstructor.Invoke([owner, update]);
            var panel = window.Content as StackPanel ?? throw new InvalidOperationException("Update progress content is missing.");
            var texts = panel.Children.OfType<TextBlock>().ToList();
            var progress = panel.Children.OfType<ProgressBar>().Single();
            var cancel = panel.Children.OfType<Button>().Single();
            if (window.Width > 510 || window.Height > 250 || cancel.Content as string != "Cancel update" || texts.Count != 2)
                throw new InvalidOperationException("Update progress window is oversized or missing status details.");
            if (!IsColor(window.Background, 250, 249, 247) || !IsColor(window.Foreground, 28, 25, 23) ||
                !IsColor(cancel.Background, 231, 229, 228) || !IsColor(progress.Foreground, 41, 37, 36))
                throw new InvalidOperationException("Update progress window does not match the warm-neutral application palette.");

            var updateProgress = typeof(UpdateProgressWindow).GetMethod("UpdateProgress", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Update progress handler is missing.");
            updateProgress.Invoke(window, [new UpdateProgressInfo(UpdateProgressStage.Downloading, 0.42, 42_000_000, 100_000_000, 5_000_000)]);
            if (progress.Value != 42 || texts[0].Text != "42% downloaded" || !texts[1].Text.Contains("MB", StringComparison.Ordinal) || !cancel.IsEnabled)
                throw new InvalidOperationException("Detailed download progress did not render correctly.");
            updateProgress.Invoke(window, [new UpdateProgressInfo(UpdateProgressStage.Verifying, 1)]);
            if (!progress.IsIndeterminate || texts[0].Text != "Verifying download...")
                throw new InvalidOperationException("Update verification state did not render correctly.");
            updateProgress.Invoke(window, [new UpdateProgressInfo(UpdateProgressStage.Preparing, 1)]);
            if (!progress.IsIndeterminate || texts[0].Text != "Preparing portable update...")
                throw new InvalidOperationException("Update preparation state did not render correctly.");

            var availableConstructor = typeof(UpdateAvailableWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var available = (UpdateAvailableWindow)availableConstructor.Invoke([owner, update]);
            if (available.Width > 550 || available.Height > 410 || !IsColor(available.Background, 250, 249, 247))
                throw new InvalidOperationException("Update details window is oversized.");
            var prepared = new PreparedUpdate(update, "package.zip", "staging");
            var readyConstructor = typeof(UpdateReadyWindow).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var ready = (UpdateReadyWindow)readyConstructor.Invoke([owner, prepared]);
            if (ready.Width > 520 || ready.Height > 290 || !IsColor(ready.Background, 250, 249, 247))
                throw new InvalidOperationException("Update-ready window is oversized.");

            window.Close();
            available.Close();
            ready.Close();
            owner.Close();
            return true;
        });

        TestArgumentsAndTransaction();
        Console.WriteLine("UPDATE UI: quiet notification workflow, detailed progress, ready decision, transactional apply and rollback verified");
    }

    private static bool IsColor(Brush? brush, byte red, byte green, byte blue)
        => brush is SolidColorBrush solid && solid.Color == Color.FromRgb(red, green, blue);

    private static void TestArgumentsAndTransaction()
    {
        var source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var sourceExe = Path.Combine(source, "SnapAnchor.exe");
        if (!File.Exists(sourceExe)) throw new InvalidOperationException("SnapAnchor.exe is missing from the smoke-test output.");
        var version = FileVersionInfo.GetVersionInfo(sourceExe).FileVersion?.Split('.').Take(3).Aggregate((left, right) => left + "." + right)
            ?? throw new InvalidOperationException("SnapAnchor.exe has no file version.");
        var root = Path.Combine(Path.GetTempPath(), "SnapAnchorUpdateSmoke", Guid.NewGuid().ToString("N"));
        var successTarget = Path.Combine(root, "SuccessTarget");
        var failureTarget = Path.Combine(root, "FailureTarget");
        var sourceManifest = Path.Combine(source, ".snapanchor-package.json");
        var sourceProbe = Path.Combine(source, "new-only.txt");
        var originalManifest = File.Exists(sourceManifest) ? File.ReadAllText(sourceManifest) : null;
        try
        {
            File.WriteAllText(sourceProbe, "new");
            File.WriteAllText(sourceManifest, JsonSerializer.Serialize(new
            {
                version,
                files = new[] { "SnapAnchor.exe", ".snapanchor-package.json", "new-only.txt" }
            }));
            PrepareOldTarget(successTarget, sourceExe);
            PrepareOldTarget(failureTarget, sourceExe);

            var success = PortableUpdateService.ApplyAsync(new PortableUpdateRequest(
                int.MaxValue, source, successTarget, Path.Combine(root, "SuccessBackup"), version, "1.0.0")).GetAwaiter().GetResult();
            if (!success.Success || !File.Exists(Path.Combine(successTarget, "new-only.txt")) ||
                File.Exists(Path.Combine(successTarget, "old-only.txt")) ||
                !File.Exists(Path.Combine(root, "SuccessBackup", "Files", "old-only.txt")))
                throw new InvalidOperationException("Portable update transaction did not install and back up managed files correctly.");

            var failure = PortableUpdateService.ApplyAsync(new PortableUpdateRequest(
                int.MaxValue, source, failureTarget, Path.Combine(root, "FailureBackup"), "9.9.9", "1.0.0")).GetAwaiter().GetResult();
            if (failure.Success || File.Exists(Path.Combine(failureTarget, "new-only.txt")) ||
                !File.Exists(Path.Combine(failureTarget, "old-only.txt")))
                throw new InvalidOperationException("Portable update rollback did not restore the previous managed files.");

            var denied = new IOException("replace failed", new UnauthorizedAccessException("denied"));
            if (!PortableUpdateService.ShouldRetryAsAdministrator(denied, null, isElevated: false) ||
                PortableUpdateService.ShouldRetryAsAdministrator(denied, new IOException("rollback failed"), isElevated: false) ||
                PortableUpdateService.ShouldRetryAsAdministrator(denied, null, isElevated: true) ||
                PortableUpdateService.ShouldRetryAsAdministrator(new IOException("disk full"), null, isElevated: false))
                throw new InvalidOperationException("Portable update administrator retry classification is unsafe.");

            var arguments = new[] { PortableUpdateRequest.Command, "--parent-pid", "123", "--source", source,
                "--target", successTarget, "--backup", Path.Combine(root, "ArgsBackup"), "--expected-version", version,
                "--previous-version", "1.0.0" };
            if (!PortableUpdateRequest.TryParse(arguments, out var request, out _) || request.ParentProcessId != 123)
                throw new InvalidOperationException("Portable updater command arguments were not parsed safely.");
            var notice = PortableUpdateService.ExtractStartupNotice(["--updated-from", "1.0.0", "history"], out var remaining);
            if (notice is not { Success: true } || remaining.Length != 1 || remaining[0] != "history")
                throw new InvalidOperationException("Post-update startup notification arguments were not preserved correctly.");
        }
        finally
        {
            try { File.Delete(sourceProbe); } catch { }
            try
            {
                if (originalManifest is null) File.Delete(sourceManifest);
                else File.WriteAllText(sourceManifest, originalManifest);
            }
            catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void PrepareOldTarget(string target, string sourceExe)
    {
        Directory.CreateDirectory(target);
        File.Copy(sourceExe, Path.Combine(target, "SnapAnchor.exe"));
        File.WriteAllText(Path.Combine(target, "old-only.txt"), "old");
        File.WriteAllText(Path.Combine(target, ".snapanchor-package.json"), JsonSerializer.Serialize(new
        {
            version = "1.0.0",
            files = new[] { "SnapAnchor.exe", ".snapanchor-package.json", "old-only.txt" }
        }));
    }
}
