using SnapPin.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapPin.Windows;

internal sealed class LanguageRestartWindow : Window
{
    private LanguageRestartWindow(Window owner, string language)
    {
        Owner = owner;
        Title = LocalizationService.Translate("Restart required", language);
        Width = 500;
        Height = 230;
        MinWidth = 420;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 29));
        ShowInTaskbar = false;
        DpiLayoutService.Attach(this);

        var message = new TextBlock
        {
            Text = LocalizationService.Translate(
                "The interface language has changed. Restart SnapPin now to apply it throughout the app?\n\nChoose Later to apply it the next time SnapPin starts.", language),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            LineHeight = 22,
            Margin = new Thickness(0, 0, 0, 22)
        };

        var later = new Button
        {
            Content = LocalizationService.Translate("Later", language),
            MinWidth = 92,
            Height = 34,
            Padding = new Thickness(14, 4, 14, 4),
            IsCancel = true
        };
        var restart = new Button
        {
            Content = LocalizationService.Translate("Restart now", language),
            MinWidth = 108,
            Height = 34,
            Padding = new Thickness(14, 4, 14, 4),
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(44, 151, 142)),
            Foreground = Brushes.White
        };
        later.Click += (_, _) => DialogResult = false;
        restart.Click += (_, _) => DialogResult = true;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        actions.Children.Add(later);
        actions.Children.Add(restart);

        var panel = new DockPanel { Margin = new Thickness(26, 24, 26, 20) };
        DockPanel.SetDock(actions, Dock.Bottom);
        panel.Children.Add(actions);
        panel.Children.Add(message);
        Content = panel;
    }

    internal static bool Ask(Window owner, string language) =>
        new LanguageRestartWindow(owner, language).ShowDialog() == true;
}
