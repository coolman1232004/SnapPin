using SnapPin.Services;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapPin.Windows;

internal sealed class UpdateProgressWindow : Window
{
    private readonly UpdateCheckResult _update;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;
    private bool _running;
    private bool _completed;
    private Exception? _error;

    private UpdateProgressWindow(Window owner, UpdateCheckResult update)
    {
        Owner = owner;
        _update = update;
        Title = LocalizationService.Current("SnapPin update");
        Width = 460;
        Height = 205;
        MinWidth = 400;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 29));
        DpiLayoutService.Attach(this);

        _status = new TextBlock
        {
            Text = LocalizationService.Format("Downloading {0}", $"SnapPin {update.Version}"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14)
        };
        _progress = new ProgressBar
        {
            Height = 12,
            Minimum = 0,
            Maximum = 100,
            Margin = new Thickness(0, 0, 0, 18)
        };
        _cancel = new Button
        {
            Content = LocalizationService.Current("Cancel update"),
            MinWidth = 104,
            Height = 34,
            Padding = new Thickness(14, 4, 14, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = false
        };
        _cancel.Click += (_, _) => CancelUpdate();

        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        panel.Children.Add(_status);
        panel.Children.Add(_progress);
        panel.Children.Add(_cancel);
        Content = panel;

        Loaded += BeginUpdate;
        Closing += (_, args) =>
        {
            if (!_running) return;
            args.Cancel = true;
            CancelUpdate();
        };
    }

    internal static bool Run(Window owner, UpdateCheckResult update)
    {
        var window = new UpdateProgressWindow(owner, update);
        window.ShowDialog();
        if (window._error is not null) ExceptionDispatchInfo.Capture(window._error).Throw();
        return window._completed;
    }

    private async void BeginUpdate(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _running = true;
        try
        {
            var progress = new Progress<UpdateProgressInfo>(UpdateProgress);
            await UpdateService.DownloadAndLaunchAsync(_update, progress, _cancellation.Token);
            _completed = true;
        }
        catch (OperationCanceledException)
        {
            _status.Text = LocalizationService.Current("The update was cancelled.");
        }
        catch (Exception ex)
        {
            _error = ex;
        }
        finally
        {
            _running = false;
            DialogResult = _completed;
        }
    }

    private void UpdateProgress(UpdateProgressInfo value)
    {
        if (value.Stage == UpdateProgressStage.Preparing)
        {
            _status.Text = LocalizationService.Current("Preparing update…");
            _progress.IsIndeterminate = true;
            _cancel.IsEnabled = false;
            return;
        }

        var percent = Math.Clamp((int)Math.Round(value.Fraction * 100), 0, 100);
        _status.Text = LocalizationService.Format("{0}% downloaded", percent);
        _progress.IsIndeterminate = false;
        _progress.Value = percent;
    }

    private void CancelUpdate()
    {
        if (!_running || _cancellation.IsCancellationRequested) return;
        _cancel.IsEnabled = false;
        _cancellation.Cancel();
    }
}
