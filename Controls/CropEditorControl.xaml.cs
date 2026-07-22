using SnapAnchor.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SnapAnchor.Controls;

public partial class CropEditorControl : UserControl
{
    private BitmapSource _source = null!;
    private Point _start;
    private Rect _selection;
    private bool _dragging;
    public event Action<BitmapSource>? Applied;
    public event EventHandler? Cancelled;

    public CropEditorControl()
    {
        InitializeComponent();
        ImageArea.SizeChanged += (_, _) =>
        {
            if (SourceImage.Source is not null && _selection.IsEmpty && ImageArea.ActualWidth > 0)
            {
                _selection = new Rect(0, 0, ImageArea.ActualWidth, ImageArea.ActualHeight);
                RenderSelection();
            }
        };
    }

    public void LoadImage(BitmapSource source)
    {
        _source = source;
        SourceImage.Source = source;
        _selection = Rect.Empty;
    }

    private void Area_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = Clamp(e.GetPosition(ImageArea));
        _selection = new Rect(_start, _start);
        _dragging = true;
        ImageArea.CaptureMouse();
        RenderSelection();
    }

    private void Area_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var point = Clamp(e.GetPosition(ImageArea));
        _selection = new Rect(new Point(Math.Min(_start.X, point.X), Math.Min(_start.Y, point.Y)),
            new Point(Math.Max(_start.X, point.X), Math.Max(_start.Y, point.Y)));
        RenderSelection();
    }

    private void Area_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ImageArea.ReleaseMouseCapture();
        if (_selection.Width < 2 || _selection.Height < 2)
            _selection = new Rect(0, 0, ImageArea.ActualWidth, ImageArea.ActualHeight);
        RenderSelection();
    }

    private void RenderSelection()
    {
        Canvas.SetLeft(CropRect, _selection.X); Canvas.SetTop(CropRect, _selection.Y);
        CropRect.Width = _selection.Width; CropRect.Height = _selection.Height;
        var pixelWidth = (int)Math.Round(_selection.Width * _source.PixelWidth / Math.Max(1, ImageArea.ActualWidth));
        var pixelHeight = (int)Math.Round(_selection.Height * _source.PixelHeight / Math.Max(1, ImageArea.ActualHeight));
        SizeText.Text = $"{pixelWidth} × {pixelHeight}";
        Canvas.SetLeft(SizeBadge, _selection.X); Canvas.SetTop(SizeBadge, Math.Max(2, _selection.Y - 25));
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_selection.Width < 2 || _selection.Height < 2) return;
        var region = new Int32Rect(
            (int)Math.Round(_selection.X * _source.PixelWidth / Math.Max(1, ImageArea.ActualWidth)),
            (int)Math.Round(_selection.Y * _source.PixelHeight / Math.Max(1, ImageArea.ActualHeight)),
            Math.Max(1, (int)Math.Round(_selection.Width * _source.PixelWidth / Math.Max(1, ImageArea.ActualWidth))),
            Math.Max(1, (int)Math.Round(_selection.Height * _source.PixelHeight / Math.Max(1, ImageArea.ActualHeight))));
        Applied?.Invoke(CaptureService.Crop(_source, region));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);
    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, ImageArea.ActualWidth), Math.Clamp(point.Y, 0, ImageArea.ActualHeight));
}
