using Microsoft.Win32;
using SnapAnchor.Models;
using SnapAnchor.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;


namespace SnapAnchor.Controls;

public partial class AnnotationEditorControl
{
    private void BeginTextEditor(Point point, AnnotationItem? existing = null)
    {
        _editingTextId = existing?.Id;
        var surfaceWidth = double.IsNaN(Surface.Width) || Surface.Width <= 0 ? Surface.ActualWidth : Surface.Width;
        var surfaceHeight = double.IsNaN(Surface.Height) || Surface.Height <= 0 ? Surface.ActualHeight : Surface.Height;
        var availableWidth = Math.Max(40, surfaceWidth - point.X);
        var availableHeight = Math.Max(28, surfaceHeight - point.Y);
        var requestedWidth = existing is null ? Math.Min(300, availableWidth) : Math.Min(Math.Max(80, existing.Width), availableWidth);
        var requestedHeight = existing is null ? Math.Min(120, availableHeight) : Math.Min(Math.Max(40, existing.Height), availableHeight);
        _textEditor = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(5, 3, 5, 3),
            CaretBrush = BrushFrom(SelectedColor()),
            SelectionBrush = new SolidColorBrush(Color.FromArgb(88, 44, 151, 142)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalContentAlignment = VerticalAlignment.Top,
            AcceptsTab = false
        };
        if (existing is not null)
        {
            SyncStyleControls(existing);
            _textEditor.Text = existing.Text;
            _textEditor.SelectAll();
        }
        ApplyTextEditorStyle();
        SpellCheck.SetIsEnabled(_textEditor, false);
        _textEditor.KeyDown += TextEditor_KeyDown;

        _textEditorFrame = new Grid
        {
            Width = requestedWidth,
            Height = requestedHeight,
            MinWidth = 80,
            MinHeight = 40,
            Background = Brushes.Transparent,
            ClipToBounds = false
        };
        _textEditorFrame.Children.Add(_textEditor);
        _textEditorFrame.Children.Add(new Shapes.Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(17, 24, 29)),
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
            SnapsToDevicePixels = true
        });
        AddTextResizeThumb("NW", HorizontalAlignment.Left, VerticalAlignment.Top, Cursors.SizeNWSE);
        AddTextResizeThumb("N", HorizontalAlignment.Center, VerticalAlignment.Top, Cursors.SizeNS);
        AddTextResizeThumb("NE", HorizontalAlignment.Right, VerticalAlignment.Top, Cursors.SizeNESW);
        AddTextResizeThumb("E", HorizontalAlignment.Right, VerticalAlignment.Center, Cursors.SizeWE);
        AddTextResizeThumb("SE", HorizontalAlignment.Right, VerticalAlignment.Bottom, Cursors.SizeNWSE);
        AddTextResizeThumb("S", HorizontalAlignment.Center, VerticalAlignment.Bottom, Cursors.SizeNS);
        AddTextResizeThumb("SW", HorizontalAlignment.Left, VerticalAlignment.Bottom, Cursors.SizeNESW);
        AddTextResizeThumb("W", HorizontalAlignment.Left, VerticalAlignment.Center, Cursors.SizeWE);

        RedrawItemsForTextEditing(existing?.Id);
        Canvas.SetLeft(_textEditorFrame, point.X);
        Canvas.SetTop(_textEditorFrame, point.Y);
        Panel.SetZIndex(_textEditorFrame, 1500);
        AnnotationCanvas.Children.Add(_textEditorFrame);
        _textEditor.Focus();
    }

    private void RedrawItemsForTextEditing(Guid? excludedId)
    {
        AnnotationCanvas.Children.Clear();
        _renderedElements.Clear();
        foreach (var item in _items.Where(item => item.Id != excludedId))
        {
            if (CreateElement(item) is not { } element) continue;
            AnnotationCanvas.Children.Add(element);
            _renderedElements[item.Id] = element;
        }
    }

    private void AddTextResizeThumb(string handle, HorizontalAlignment horizontal, VerticalAlignment vertical, Cursor cursor)
    {
        if (_textEditorFrame is null) return;
        var thumb = new Thumb
        {
            Width = 7,
            Height = 7,
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(17, 24, 29)),
            BorderThickness = new Thickness(1),
            Cursor = cursor,
            Tag = handle
        };
        thumb.DragDelta += (_, e) => ResizeTextEditorFrame(handle, e.HorizontalChange, e.VerticalChange);
        Panel.SetZIndex(thumb, 2);
        _textEditorFrame.Children.Add(thumb);
    }

    private void StartTextEditorDrag(Point point)
    {
        BeginTextEditor(point);
        if (_textEditorFrame is null || _textEditor is null) return;
        _textBoxDragging = true;
        _textBoxStart = point;
        _textEditorFrame.MinWidth = 1;
        _textEditorFrame.MinHeight = 1;
        _textEditorFrame.Width = 1;
        _textEditorFrame.Height = 1;
        _textEditor.IsHitTestVisible = false;
        Surface.CaptureMouse();
    }

    private void UpdateTextEditorDrag(Point point)
    {
        if (_textEditorFrame is null) return;
        var left = Math.Min(_textBoxStart.X, point.X);
        var top = Math.Min(_textBoxStart.Y, point.Y);
        _textEditorFrame.Width = Math.Max(1, Math.Abs(point.X - _textBoxStart.X));
        _textEditorFrame.Height = Math.Max(1, Math.Abs(point.Y - _textBoxStart.Y));
        Canvas.SetLeft(_textEditorFrame, left);
        Canvas.SetTop(_textEditorFrame, top);
    }

    private void FinishTextEditorDrag()
    {
        _textBoxDragging = false;
        Surface.ReleaseMouseCapture();
        if (_textEditorFrame is null || _textEditor is null) return;
        var surfaceWidth = Math.Max(1, Surface.Width);
        var surfaceHeight = Math.Max(1, Surface.Height);
        var left = Math.Max(0, Canvas.GetLeft(_textEditorFrame));
        var top = Math.Max(0, Canvas.GetTop(_textEditorFrame));
        if (_textEditorFrame.Width < 12 || _textEditorFrame.Height < 12)
        {
            _textEditorFrame.Width = Math.Min(300, Math.Max(40, surfaceWidth - left));
            _textEditorFrame.Height = Math.Min(120, Math.Max(28, surfaceHeight - top));
        }
        else
        {
            _textEditorFrame.Width = Math.Min(Math.Max(80, _textEditorFrame.Width), Math.Max(40, surfaceWidth - left));
            _textEditorFrame.Height = Math.Min(Math.Max(40, _textEditorFrame.Height), Math.Max(28, surfaceHeight - top));
        }
        _textEditorFrame.MinWidth = 40;
        _textEditorFrame.MinHeight = 28;
        _textEditor.IsHitTestVisible = true;
        _textEditor.Focus();
    }

    private void ResizeTextEditorFrame(string handle, double dx, double dy)
    {
        if (_textEditorFrame is null) return;
        var surfaceWidth = Math.Max(1, Surface.Width);
        var surfaceHeight = Math.Max(1, Surface.Height);
        var left = Canvas.GetLeft(_textEditorFrame);
        var top = Canvas.GetTop(_textEditorFrame);
        var right = left + _textEditorFrame.ActualWidth;
        var bottom = top + _textEditorFrame.ActualHeight;
        if (handle.Contains('W')) left = Math.Clamp(left + dx, 0, right - 40);
        if (handle.Contains('E')) right = Math.Clamp(right + dx, left + 40, surfaceWidth);
        if (handle.Contains('N')) top = Math.Clamp(top + dy, 0, bottom - 28);
        if (handle.Contains('S')) bottom = Math.Clamp(bottom + dy, top + 28, surfaceHeight);
        Canvas.SetLeft(_textEditorFrame, left);
        Canvas.SetTop(_textEditorFrame, top);
        _textEditorFrame.Width = right - left;
        _textEditorFrame.Height = bottom - top;
    }

    private void ApplyTextEditorStyle()
    {
        if (_textEditor is null) return;
        _textEditor.FontSize = Math.Max(9, ThicknessSlider.Value * 4);
        _textEditor.FontFamily = new FontFamily(TextFontBox.SelectedValue as string ?? "Segoe UI");
        _textEditor.FontWeight = _textBold ? FontWeights.Bold : FontWeights.Normal;
        _textEditor.FontStyle = _textItalic ? FontStyles.Italic : FontStyles.Normal;
        _textEditor.TextDecorations = TextDecorationsFor(_textUnderline, _textStrikethrough);
        _textEditor.TextAlignment = _textAlignment;
        _textEditor.Foreground = BrushFrom(SelectedColor());
        _textEditor.CaretBrush = BrushFrom(SelectedColor());
        _textEditor.Effect = _textOutline
            ? new DropShadowEffect { Color = Colors.White, BlurRadius = 1.5, ShadowDepth = 0, Opacity = 1 }
            : null;
    }

    private void TextEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelTextEditor();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            CommitTextEditor();
            e.Handled = true;
        }
    }

    private void CommitTextEditor()
    {
        var editor = _textEditor;
        if (editor is null) return;
        var frame = _textEditorFrame;
        _textEditor = null;
        _textEditorFrame = null;
        _textBoxDragging = false;
        if (Mouse.Captured == Surface) Surface.ReleaseMouseCapture();
        var text = editor.Text.TrimEnd();
        var point = frame is null
            ? new Point()
            : new Point(Math.Max(0, Canvas.GetLeft(frame)), Math.Max(0, Canvas.GetTop(frame)));
        var width = frame is null ? 120 : Math.Max(40, frame.ActualWidth > 0 ? frame.ActualWidth : frame.Width);
        var height = frame is null ? 38 : Math.Max(28, frame.ActualHeight > 0 ? frame.ActualHeight : frame.Height);
        if (!string.IsNullOrWhiteSpace(text) && _editingTextId is Guid existingId && _items.FirstOrDefault(item => item.Id == existingId) is { } existing)
        {
            existing.Text = text;
            existing.X = point.X;
            existing.Y = point.Y;
            existing.Width = _pendingTextKind == AnnotationKind.Callout ? Math.Max(220, width) : width;
            existing.Height = _pendingTextKind == AnnotationKind.Callout ? Math.Max(80, height) : height;
            existing.Color = SelectedColor();
            existing.Thickness = ThicknessSlider.Value;
            ApplyCurrentStyle(existing);
            _selectedId = existing.Id;
        }
        else if (string.IsNullOrWhiteSpace(text) && _editingTextId is Guid emptyId)
        {
            _items.RemoveAll(item => item.Id == emptyId);
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            PushUndo();
            var item = new AnnotationItem
            {
                Kind = _pendingTextKind,
                X = point.X,
                Y = point.Y,
                Width = _pendingTextKind == AnnotationKind.Callout ? Math.Max(220, width) : width,
                Height = _pendingTextKind == AnnotationKind.Callout ? Math.Max(80, height) : height,
                Text = text,
                Color = SelectedColor(),
                Thickness = ThicknessSlider.Value
            };
            ApplyCurrentStyle(item);
            if (item.Kind == AnnotationKind.Callout) item.FillOpacity = Math.Max(0.18, item.FillOpacity);
            _items.Add(item);
            _selectedId = item.Id;
        }
        _editingTextId = null;
        _pendingTextKind = AnnotationKind.Text;
        _selectedId = null;
        RenderAnnotations();
    }

    private void CancelTextEditor()
    {
        _textBoxDragging = false;
        if (Mouse.Captured == Surface) Surface.ReleaseMouseCapture();
        _textEditor = null;
        _textEditorFrame = null;
        _editingTextId = null;
        RenderAnnotations();
    }

}
