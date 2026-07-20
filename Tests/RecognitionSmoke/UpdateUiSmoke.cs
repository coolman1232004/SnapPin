using SnapPin.Services;
using SnapPin.Windows;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace SnapPin.RecognitionSmoke;

internal static class UpdateUiSmoke
{
    internal static void Run()
    {
        Program.RunSta(() =>
        {
            LocalizationService.Configure(LocalizationService.English);
            var constructor = typeof(UpdateProgressWindow)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single();
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
                PackageKind: UpdatePackageKind.Portable);
            var window = (UpdateProgressWindow)constructor.Invoke([owner, update]);
            var panel = window.Content as StackPanel ?? throw new InvalidOperationException("Update progress content is missing.");
            var status = panel.Children.OfType<TextBlock>().Single();
            var progress = panel.Children.OfType<ProgressBar>().Single();
            var cancel = panel.Children.OfType<Button>().Single();
            if (window.Width > 500 || window.Height > 240 || cancel.Content as string != "Cancel update")
                throw new InvalidOperationException("Update progress window is oversized or missing its cancel action.");

            var updateProgress = typeof(UpdateProgressWindow).GetMethod("UpdateProgress", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Update progress handler is missing.");
            updateProgress.Invoke(window, [new UpdateProgressInfo(UpdateProgressStage.Downloading, 0.42)]);
            if (progress.Value != 42 || status.Text != "42% downloaded" || !cancel.IsEnabled)
                throw new InvalidOperationException("Determinate update progress did not render correctly.");
            updateProgress.Invoke(window, [new UpdateProgressInfo(UpdateProgressStage.Preparing, 1)]);
            if (!progress.IsIndeterminate || cancel.IsEnabled || status.Text != "Preparing update…")
                throw new InvalidOperationException("Update preparation state did not render correctly.");
            window.Close();
            owner.Close();
            return true;
        });
        Console.WriteLine("UPDATE UI: percentage, cancel action and preparation state verified");
    }
}
