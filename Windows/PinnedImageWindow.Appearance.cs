using Microsoft.Win32;
using SnapAnchor.Models;
using SnapAnchor.Services;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;


namespace SnapAnchor.Windows;

public partial class PinnedImageWindow
{
    private void ApplyTransform()
    {
        var transforms = new TransformGroup();
        transforms.Children.Add(new ScaleTransform(_flipX, _flipY));
        transforms.Children.Add(new RotateTransform(_rotation));
        transforms.Freeze();
        PinnedImage.LayoutTransform = transforms;
        LongPinnedImage.LayoutTransform = transforms;
        SelectableTextLayer.LayoutTransform = transforms;
        LongSelectableTextLayer.LayoutTransform = transforms;
        if (_textSelectable) Dispatcher.BeginInvoke(UpdateSelectableTextLayout, DispatcherPriority.Loaded);
    }

    private void ResetAppearance()
    {
        _rotation = 0; _flipX = _flipY = 1; _grayscale = _inverted = false;
        Opacity = 1; Topmost = true; PinnedImage.LayoutTransform = Transform.Identity;
        LongPinnedImage.LayoutTransform = Transform.Identity;
        SelectableTextLayer.LayoutTransform = Transform.Identity;
        LongSelectableTextLayer.LayoutTransform = Transform.Identity;
        StopZoomAnimation(false);
        ApplyFilters(); SizeToImage(_source);
        _targetZoomBounds = new Rect(Left, Top, Width, Height);
        SetClickThrough(false);
    }

    private void ShowImageSurface()
    {
        ShadeHeader.Visibility = _shaded ? Visibility.Visible : Visibility.Collapsed;
        if (_shaded)
        {
            PinnedImage.Visibility = Visibility.Collapsed;
            LongImageViewer.Visibility = Visibility.Collapsed;
            SelectableTextLayer.Visibility = Visibility.Collapsed;
            LongSelectableTextLayer.Visibility = Visibility.Collapsed;
            TextSelectionToolbar.Visibility = Visibility.Collapsed;
            UpdatePinStateBadge();
            return;
        }
        var useLongViewer = _longScrollable && !_thumbnail;
        LongImageViewer.Visibility = useLongViewer ? Visibility.Visible : Visibility.Collapsed;
        PinnedImage.Visibility = useLongViewer ? Visibility.Collapsed : Visibility.Visible;
        if (useLongViewer) UpdateLongImageWidth();
        SelectableTextLayer.Visibility = _textSelectable && !useLongViewer ? Visibility.Visible : Visibility.Collapsed;
        LongSelectableTextLayer.Visibility = _textSelectable && useLongViewer ? Visibility.Visible : Visibility.Collapsed;
        if (_textSelectable) Dispatcher.BeginInvoke(UpdateSelectableTextLayout, DispatcherPriority.Loaded);
        UpdatePinStateBadge();
    }

    private void UpdateLongImageWidth()
    {
        if (!_longScrollable || ActualWidth <= 1) return;
        var width = Math.Max(1, ActualWidth - 16);
        var height = width * _source.PixelHeight / Math.Max(1.0, _source.PixelWidth);
        LongImageSurface.Width = width;
        LongImageSurface.Height = height;
        LongPinnedImage.Width = width;
        LongPinnedImage.Height = height;
        LongSelectableTextLayer.Width = width;
        LongSelectableTextLayer.Height = height;
        if (_textSelectable) UpdateSelectableTextLayer(LongSelectableTextLayer);
    }

    private void LongImageViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_longScrollable || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
        var basis = _longScrollAnimationTimer.IsEnabled ? _targetLongScrollOffset : LongImageViewer.VerticalOffset;
        _targetLongScrollOffset = Math.Clamp(basis - e.Delta * 0.62, 0, Math.Max(0, LongImageViewer.ScrollableHeight));
        _longScrollAnimationTimer.Start();
        e.Handled = true;
    }

    private void LongScrollAnimationTimer_Tick(object? sender, EventArgs e)
    {
        var current = LongImageViewer.VerticalOffset;
        var next = current + (_targetLongScrollOffset - current) * 0.36;
        if (Math.Abs(_targetLongScrollOffset - next) < 0.45)
        {
            next = _targetLongScrollOffset;
            _longScrollAnimationTimer.Stop();
        }
        LongImageViewer.ScrollToVerticalOffset(next);
    }

}
