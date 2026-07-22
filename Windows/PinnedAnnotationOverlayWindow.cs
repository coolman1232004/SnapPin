using SnapAnchor.Controls;
using SnapAnchor.Models;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SnapAnchor.Windows;

/// <summary>
/// A transparent, virtual-desktop overlay that keeps the original pin visible
/// while hosting the same annotation toolbar used by region capture.
/// </summary>
internal sealed class PinnedAnnotationOverlayWindow : Window
{
    private readonly AnnotationEditorControl _editor;

    internal event Action<AnnotationAppliedEventArgs>? Applied;
    internal event Action<AnnotationAppliedEventArgs>? DocumentStored;

    internal PinnedAnnotationOverlayWindow(
        BitmapSource source,
        IEnumerable<AnnotationItem>? items,
        Rect pinScreenBounds)
    {
        Title = "SnapAnchor Pin toolbar";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        _editor = new AnnotationEditorControl();
        Content = _editor;
        _editor.LoadImage(source, items);
        _editor.SetExternalBackgroundMode(true);

        var relativePinBounds = new Rect(
            pinScreenBounds.Left - Left,
            pinScreenBounds.Top - Top,
            pinScreenBounds.Width,
            pinScreenBounds.Height);
        _editor.ConfigureCaptureOverlay(
            relativePinBounds,
            new Size(Width, Height),
            relativePinBounds,
            showActions: true,
            showPrimaryToolbar: true,
            showCancelAction: true,
            startWithNoTool: true,
            allowToolToggleOff: true);

        _editor.ExternalSurfaceMoved += requestedDelta =>
        {
            if (Owner is not PinnedImageWindow pin) return;
            var actualDelta = pin.MoveFromAnnotationOverlay(requestedDelta);
            _editor.TranslateCaptureOverlay(actualDelta);
        };
        _editor.ExternalSurfaceMoveCompleted += () =>
        {
            if (Owner is not PinnedImageWindow pin) return;
            var snapDelta = pin.CompleteAnnotationOverlayMove();
            _editor.TranslateCaptureOverlay(snapDelta);
        };

        _editor.Applied += document =>
        {
            Applied?.Invoke(document);
            Close();
        };
        _editor.DocumentStored += document => DocumentStored?.Invoke(document);
        _editor.Cancelled += (_, _) => Close();
        Loaded += (_, _) =>
        {
            AlignEditorToOwnerPin();
            Activate();
            _editor.Focus();
        };
    }

    private void AlignEditorToOwnerPin()
    {
        if (Owner is not PinnedImageWindow pin) return;
        pin.UpdateLayout();
        UpdateLayout();

        var screenTopLeft = pin.PointToScreen(new Point(0, 0));
        var screenBottomRight = pin.PointToScreen(new Point(
            Math.Max(1, pin.ActualWidth),
            Math.Max(1, pin.ActualHeight)));
        var overlayTopLeft = PointFromScreen(screenTopLeft);
        var overlayBottomRight = PointFromScreen(screenBottomRight);
        var actualPinBounds = new Rect(
            overlayTopLeft,
            new Size(
                Math.Max(1, overlayBottomRight.X - overlayTopLeft.X),
                Math.Max(1, overlayBottomRight.Y - overlayTopLeft.Y)));

        _editor.UpdateCaptureOverlayBounds(
            actualPinBounds,
            new Size(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)),
            actualPinBounds,
            resetToolbarPosition: true);
    }
}
