using SnapPin.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapPin.Windows;

internal enum UpdateReadyDecision
{
    Later,
    ApplyNow
}

internal sealed class UpdateReadyWindow : Window
{
    private UpdateReadyDecision _decision;

    private UpdateReadyWindow(Window owner, PreparedUpdate prepared)
    {
        Owner = owner;
        Title = LocalizationService.Current("Update ready");
        Width = 500;
        Height = 270;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 29));
        DpiLayoutService.Attach(this);

        var root = new Grid { Margin = new Thickness(28, 24, 28, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(new TextBlock
        {
            Text = LocalizationService.Format("SnapPin {0} is ready", prepared.Update.Version),
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14)
        });
        var body = new TextBlock
        {
            Text = prepared.Update.PackageKind == UpdatePackageKind.Portable
                ? LocalizationService.Current("The update is downloaded, verified, and prepared. Restart SnapPin now to replace the portable files, or choose Later to keep using this version.")
                : LocalizationService.Current("The installer is downloaded and verified. Install it now, or choose Later to keep using this version."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99)),
            LineHeight = 21
        };
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var later = new Button
        {
            Content = LocalizationService.Current("Later"),
            MinWidth = 100,
            Height = 36,
            Background = new SolidColorBrush(Color.FromRgb(225, 231, 235)),
            Margin = new Thickness(0, 0, 10, 0),
            IsCancel = true
        };
        later.Click += (_, _) => { _decision = UpdateReadyDecision.Later; DialogResult = false; };
        var apply = new Button
        {
            Content = LocalizationService.Current(prepared.Update.PackageKind == UpdatePackageKind.Portable ? "Restart and update" : "Install update"),
            MinWidth = 142,
            Height = 36,
            Background = new SolidColorBrush(Color.FromRgb(44, 151, 142)),
            Foreground = Brushes.White,
            IsDefault = true
        };
        apply.Click += (_, _) => { _decision = UpdateReadyDecision.ApplyNow; DialogResult = true; };
        buttons.Children.Add(later);
        buttons.Children.Add(apply);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    internal static UpdateReadyDecision Ask(Window owner, PreparedUpdate prepared)
    {
        var window = new UpdateReadyWindow(owner, prepared);
        window.ShowDialog();
        return window._decision;
    }
}
