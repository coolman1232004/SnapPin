using SnapPin.Models;
using SnapPin.Services;
using System.Globalization;
using System.Windows;

namespace SnapPin.Windows;

public partial class CustomCaptureWindow : Window
{
    internal CaptureOptions Options { get; private set; } = new();

    public CustomCaptureWindow()
    {
        InitializeComponent();
        DpiLayoutService.Attach(this);
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        Options = new CaptureOptions
        {
            FixedWidth = ParseInt(WidthBox.Text, 0, 50000),
            FixedHeight = ParseInt(HeightBox.Text, 0, 50000),
            DelaySeconds = ParseInt(DelayBox.Text, 0, 60),
            AspectRatio = double.TryParse(RatioBox.SelectedValue as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ? ratio : 0,
            IncludeCursor = CursorBox.IsChecked == true
        };
        DialogResult = true;
    }

    private static int ParseInt(string text, int minimum, int maximum) =>
        int.TryParse(text, out var value) ? Math.Clamp(value, minimum, maximum) : minimum;

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
