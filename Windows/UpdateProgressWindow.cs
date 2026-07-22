using SnapAnchor.Services;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapAnchor.Windows;

internal sealed class UpdateProgressWindow : Window
{
    private readonly UpdateCheckResult _update;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TextBlock _status;
    private readonly TextBlock _detail;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;
    private bool _running;
    private PreparedUpdate? _prepared;
    private Exception? _error;

    private UpdateProgressWindow(Window owner, UpdateCheckResult update)
    {
        Owner = owner;
        _update = update;
        Title = LocalizationService.Current("SnapAnchor update");
        Width = 490;
        Height = 235;
        MinWidth = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 29));
        DpiLayoutService.Attach(this);

        _status = new TextBlock
        {
            Text = LocalizationService.Format("Downloading SnapAnchor {0}...", update.Version),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7)
        };
        _detail = new TextBlock
        {
            Text = update.DownloadSize > 0 ? LocalizationService.Format("Download size: {0}", UpdateService.FormatBytes(update.DownloadSize)) : string.Empty,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            Margin = new Thickness(0, 0, 0, 15)
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
            Background = new SolidColorBrush(Color.FromRgb(225, 231, 235)),
            IsCancel = false
        };
        _cancel.Click += (_, _) => CancelUpdate();

        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 22) };
        panel.Children.Add(_status);
        panel.Children.Add(_detail);
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

    internal static PreparedUpdate? Run(Window owner, UpdateCheckResult update)
    {
        var window = new UpdateProgressWindow(owner, update);
        window.ShowDialog();
        if (window._error is not null) ExceptionDispatchInfo.Capture(window._error).Throw();
        return window._prepared;
    }

    private async void BeginUpdate(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _running = true;
        try
        {
            var progress = new Progress<UpdateProgressInfo>(UpdateProgress);
            _prepared = await UpdateService.PrepareAsync(_update, progress, _cancellation.Token);
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
            DialogResult = _prepared is not null;
        }
    }

    private void UpdateProgress(UpdateProgressInfo value)
    {
        switch (value.Stage)
        {
            case UpdateProgressStage.Verifying:
                _status.Text = LocalizationService.Current("Verifying download...");
                _detail.Text = LocalizationService.Current("Checking the package integrity before any files are changed.");
                _progress.IsIndeterminate = true;
                _cancel.IsEnabled = true;
                return;
            case UpdateProgressStage.Preparing:
                _status.Text = LocalizationService.Current("Preparing portable update...");
                _detail.Text = LocalizationService.Current("Extracting and validating the update in a temporary folder.");
                _progress.IsIndeterminate = true;
                _cancel.IsEnabled = true;
                return;
        }

        var percent = Math.Clamp((int)Math.Round(value.Fraction * 100), 0, 100);
        _status.Text = LocalizationService.Format("{0}% downloaded", percent);
        _progress.IsIndeterminate = value.TotalBytes <= 0;
        _progress.Value = percent;
        if (value.BytesReceived <= 0) return;
        var transferred = UpdateService.FormatBytes(value.BytesReceived);
        var total = value.TotalBytes > 0 ? UpdateService.FormatBytes(value.TotalBytes) : LocalizationService.Current("Unknown size");
        var speed = value.BytesPerSecond > 0 ? UpdateService.FormatBytes((long)value.BytesPerSecond) + "/s" : "-";
        var remaining = value.BytesPerSecond > 0 && value.TotalBytes > value.BytesReceived
            ? TimeSpan.FromSeconds((value.TotalBytes - value.BytesReceived) / value.BytesPerSecond)
            : TimeSpan.Zero;
        var eta = remaining > TimeSpan.Zero
            ? LocalizationService.Format("about {0} remaining", remaining.TotalMinutes >= 1 ? $"{Math.Ceiling(remaining.TotalMinutes):0} min" : $"{Math.Ceiling(remaining.TotalSeconds):0} sec")
            : string.Empty;
        _detail.Text = $"{transferred} / {total}  -  {speed}" + (eta.Length > 0 ? $"  -  {eta}" : string.Empty);
    }

    private void CancelUpdate()
    {
        if (!_running || _cancellation.IsCancellationRequested) return;
        _cancel.IsEnabled = false;
        _cancellation.Cancel();
    }
}
