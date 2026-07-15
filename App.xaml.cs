using System.Windows;
using System.IO;
using SnapPin.Services;
using SnapPin.Models;

namespace SnapPin;

public partial class App : System.Windows.Application
{
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        var startupArguments = RestartService.PrepareStartupArguments(e.Args);
        var settings = SettingsService.Load();
        LocalizationService.Configure(settings.UiLanguage);
        LocalizationService.EnableAutomaticLocalization();
        var recoveredPreviousCrash = DiagnosticsService.Start();
        DispatcherUnhandledException += (_, args) =>
        {
            DiagnosticsService.Log("ui-error", args.Exception.Message, args.Exception);
            MessageBox.Show($"SnapPin recovered from an unexpected error.\n\n{args.Exception.Message}\n\nA diagnostic log was saved locally.",
                "SnapPin recovered", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            DiagnosticsService.Log("fatal", "Unhandled application error", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticsService.Log("background-error", args.Exception.Message, args.Exception);
            args.SetObserved();
        };
        if (!AppCommand.TryParse(startupArguments, out var startupCommand, out var commandError))
        {
            MessageBox.Show(commandError, "SnapPin command", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(2);
            return;
        }
        ToolbarThemeService.Apply(settings.ToolbarSizeMode);
        if (ElevationService.NeedsElevation(settings.RunAsAdministrator, ElevationService.IsCurrentProcessElevated()))
        {
            if (ElevationService.TryRelaunchElevated(startupArguments, out var elevationError))
            {
                Shutdown();
                return;
            }
            if (!string.IsNullOrWhiteSpace(elevationError))
                MessageBox.Show(elevationError, "SnapPin administrator mode", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        var executablePath = Environment.ProcessPath ?? string.Empty;
        var developmentBuild = executablePath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        _singleInstance = new SingleInstanceService(developmentBuild ? "SnapPin.Desktop.x64.Development" : "SnapPin.Desktop.x64");
        if (!_singleInstance.IsPrimaryInstance)
        {
            _singleInstance.SignalPrimaryInstance(startupCommand);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        _singleInstance.ActivationRequested += (_, _) => Dispatcher.BeginInvoke(window.ShowFromExternalActivation);
        _singleInstance.CommandReceived += (_, args) =>
        {
            if (args.Command.Kind != AppCommandKind.Activate)
                Dispatcher.BeginInvoke(() => window.ExecuteCommand(args.Command));
        };
        _singleInstance.StartListening();
        window.Show();
        if (settings.CheckUpdatesOnStartup && !string.IsNullOrWhiteSpace(settings.UpdateFeedUrl))
            Dispatcher.BeginInvoke(async () => await CheckForUpdatesOnStartupAsync(window, settings));
        if (recoveredPreviousCrash)
            Dispatcher.BeginInvoke(() => MessageBox.Show(window,
                "SnapPin recovered after the previous unexpected close. Open pins were restored from the last verified backup.\n\nYou can review the local diagnostic log in Preferences > About.",
                "SnapPin recovery", MessageBoxButton.OK, MessageBoxImage.Information));
        if (startupCommand.Kind != AppCommandKind.Activate)
            Dispatcher.BeginInvoke(() => window.ExecuteCommand(startupCommand));
    }

    private static async Task CheckForUpdatesOnStartupAsync(Window owner, AppSettings settings)
    {
        try
        {
            var update = await UpdateService.CheckAsync(settings.UpdateFeedUrl);
            if (!update.UpdateAvailable || string.IsNullOrWhiteSpace(update.DownloadUrl)) return;
            if (MessageBox.Show(owner, update.Message + "\n\n" + LocalizationService.Current("Download and install it now?"), LocalizationService.Current("SnapPin update available"),
                MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            await UpdateService.DownloadAndLaunchAsync(update);
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            DiagnosticsService.Log("startup-update", ex.Message, ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        DiagnosticsService.MarkCleanExit();
        base.OnExit(e);
    }
}
