using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SnapPin.Services;

internal static class AccessibilityService
{
    internal static void Apply(DependencyObject root)
    {
        ApplyElement(root);
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) Apply(child);
    }

    private static void ApplyElement(DependencyObject value)
    {
        if (value is FrameworkElement element && string.IsNullOrWhiteSpace(AutomationProperties.GetName(element)))
        {
            var name = element switch
            {
                TextBlock text => text.Text,
                ContentControl { Content: string content } => content,
                HeaderedContentControl { Header: string header } => header,
                _ => element.ToolTip as string ?? string.Empty
            };
            if (!string.IsNullOrWhiteSpace(name)) AutomationProperties.SetName(element, name);
        }
        if (!SystemParameters.HighContrast) return;
        if (value is Window window)
        {
            window.Background = SystemColors.WindowBrush;
            window.Foreground = SystemColors.WindowTextBrush;
        }
        else if (value is Control control)
        {
            control.Foreground = SystemColors.WindowTextBrush;
            if (control is Button or TextBox or ComboBox or ListBox)
                control.BorderBrush = SystemColors.WindowTextBrush;
        }
    }
}
