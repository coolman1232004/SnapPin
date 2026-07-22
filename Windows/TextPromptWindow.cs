using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SnapAnchor.Windows;

internal sealed class TextPromptWindow : Window
{
    private readonly TextBox _input;

    private TextPromptWindow(Window owner, string title, string label, string initialValue)
    {
        Owner = owner;
        Title = title;
        Width = 420;
        Height = 170;
        MinWidth = 340;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39));
        _input = new TextBox { Text = initialValue, Height = 31, Padding = new Thickness(7, 4, 7, 4) };
        var ok = new Button { Content = "Save", Width = 82, Height = 31, IsDefault = true, Margin = new Thickness(8, 0, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 82, Height = 31, IsCancel = true };
        ok.Click += (_, _) => DialogResult = true;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var panel = new Grid { Margin = new Thickness(18) };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) });
        Grid.SetRow(_input, 1); panel.Children.Add(_input);
        Grid.SetRow(buttons, 2); buttons.Margin = new Thickness(0, 14, 0, 0); panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    internal static string? Ask(Window owner, string title, string label, string initialValue = "")
    {
        var dialog = new TextPromptWindow(owner, title, label, initialValue);
        return dialog.ShowDialog() == true ? dialog._input.Text.Trim() : null;
    }
}
