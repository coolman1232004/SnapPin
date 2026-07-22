using SnapAnchor.Controls;
using SnapAnchor.Models;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

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
        SourceInitialized += (_, _) => FitToVirtualScreenPixels();

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
            var ownerDelta = ConvertDelta(requestedDelta, VisualTreeHelper.GetDpi(this), VisualTreeHelper.GetDpi(pin));
            var actualOwnerDelta = pin.MoveFromAnnotationOverlay(ownerDelta);
            var overlayDelta = ConvertDelta(actualOwnerDelta, VisualTreeHelper.GetDpi(pin), VisualTreeHelper.GetDpi(this));
            _editor.TranslateCaptureOverlay(overlayDelta);
        };
        _editor.ExternalSurfaceMoveCompleted += () =>
        {
            if (Owner is not PinnedImageWindow pin) return;
            pin.CompleteAnnotationOverlayMove();
            AlignEditorToOwnerPin();
        };

        _editor.Applied += document =>
        {
            Applied?.Invoke(document);
            Close();
        };
        _editor.DocumentStored += document => DocumentStored?.Invoke(document);
        _editor.Cancelled += (_, _) => Close();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            FitToVirtualScreenPixels();
            AlignEditorToOwnerPin();
            Activate();
            _editor.Focus();
        }, DispatcherPriority.ContextIdle);
    }

    private void AlignEditorToOwnerPin()
    {
        if (Owner is not PinnedImageWindow pin) return;
        var overlayHandle = new WindowInteropHelper(this).Handle;
        var pinHandle = new WindowInteropHelper(pin).Handle;
        if (overlayHandle == IntPtr.Zero || pinHandle == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(overlayHandle, out var overlayPixels) ||
            !NativeMethods.GetWindowRect(pinHandle, out var pinPixels)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var actualPinBounds = PhysicalBoundsToOverlay(pinPixels, overlayPixels, dpi.DpiScaleX, dpi.DpiScaleY);

        _editor.UpdateCaptureOverlayBounds(
            actualPinBounds,
            new Size(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight)),
            actualPinBounds,
            resetToolbarPosition: true);
    }

    internal static Rect PhysicalBoundsToOverlay(
        NativeMethods.NativeRect target,
        NativeMethods.NativeRect overlay,
        double overlayScaleX,
        double overlayScaleY)
    {
        var scaleX = Math.Max(0.1, overlayScaleX);
        var scaleY = Math.Max(0.1, overlayScaleY);
        return new Rect(
            (target.Left - overlay.Left) / scaleX,
            (target.Top - overlay.Top) / scaleY,
            Math.Max(1, target.Width / scaleX),
            Math.Max(1, target.Height / scaleY));
    }

    private static Vector ConvertDelta(Vector delta, DpiScale from, DpiScale to)
        => new(
            delta.X * from.DpiScaleX / Math.Max(0.1, to.DpiScaleX),
            delta.Y * from.DpiScaleY / Math.Max(0.1, to.DpiScaleY));

    private void FitToVirtualScreenPixels()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var bounds = Forms.SystemInformation.VirtualScreen;
        if (handle != IntPtr.Zero)
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }
}
