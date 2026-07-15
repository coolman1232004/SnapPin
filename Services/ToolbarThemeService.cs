using System.Windows;

namespace SnapPin.Services;

internal static class ToolbarThemeService
{
    internal static readonly string[] Modes = ["Compact", "Standard", "Large"];
    private static string _currentMode = "Compact";

    internal static string Normalize(string? mode) =>
        Modes.FirstOrDefault(item => item.Equals(mode, StringComparison.OrdinalIgnoreCase)) ?? "Compact";

    internal static void Apply(string? mode)
    {
        _currentMode = Normalize(mode);
        if (Application.Current is not null)
            ApplyTo(Application.Current.Resources, _currentMode);
    }

    internal static void ApplyTo(ResourceDictionary resources, string? mode = null)
    {
        var normalized = mode is null ? _currentMode : Normalize(mode);
        var metrics = normalized switch
        {
            "Large" => new Metrics(36, 24, 18, 24, new Thickness(4), new Thickness(1.5), new Thickness(4, 2, 4, 2), new Thickness(4, 6, 4, 6)),
            "Standard" => new Metrics(30, 20, 16, 20, new Thickness(3), new Thickness(1), new Thickness(3, 2, 3, 2), new Thickness(3, 5, 3, 5)),
            _ => new Metrics(26, 18, 14, 18, new Thickness(2), new Thickness(1), new Thickness(3, 1, 3, 1), new Thickness(3, 4, 3, 4))
        };
        resources["ToolbarButtonSize"] = metrics.Button;
        resources["ToolbarIconSize"] = metrics.Icon;
        resources["ToolbarGripWidth"] = metrics.Grip;
        resources["ToolbarSeparatorHeight"] = metrics.Separator;
        resources["ToolbarButtonPadding"] = metrics.ButtonPadding;
        resources["ToolbarButtonMargin"] = metrics.ButtonMargin;
        resources["ToolbarFramePadding"] = metrics.FramePadding;
        resources["ToolbarSeparatorMargin"] = metrics.SeparatorMargin;
    }

    private readonly record struct Metrics(double Button, double Icon, double Grip, double Separator,
        Thickness ButtonPadding, Thickness ButtonMargin, Thickness FramePadding, Thickness SeparatorMargin);
}
