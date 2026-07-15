using Microsoft.Win32;
using SnapPin.Models;
using SnapPin.Services;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Shapes = System.Windows.Shapes;

namespace SnapPin.Controls;

internal sealed record AnnotationAppliedEventArgs(BitmapSource BaseImage, BitmapSource FlattenedImage, IReadOnlyList<AnnotationItem> Items);

internal sealed record AnnotationHandleTag(Guid ItemId, string Handle);

public partial class AnnotationEditorControl : UserControl
{
    private BitmapSource _source = null!;
    private BitmapSource? _blurredSource;
    private readonly AppSettings _settings;
    private List<AnnotationItem> _items = [];
    private readonly Stack<List<AnnotationItem>> _undo = [];
    private readonly Stack<List<AnnotationItem>> _redo = [];
    private readonly Dictionary<Guid, FrameworkElement> _renderedElements = [];
    private string _tool = "Select";
    private Guid? _activeId;
    private Guid? _selectedId;
    private Point _start;
    private Point _last;
    private bool _dragging;
    private TextBox? _textEditor;
    private Grid? _textEditorFrame;
    private bool _textBoxDragging;
    private Point _textBoxStart;
    private Guid? _editingTextId;
    private AnnotationKind _pendingTextKind = AnnotationKind.Text;
    private int _nextNumber = 1;
    private bool _fillEnabled;
    private bool _dashed;
    private bool _loadingStyle = true;
    private bool _renderPending;
    private string _transformMode = string.Empty;
    private Point _transformStart;
    private AnnotationItem? _transformOriginal;
    private bool _captureOverlayMode;
    private Rect _captureSurfaceBounds;
    private Rect _captureToolbarAnchor;
    private Size _captureViewport;
    private double? _captureToolbarLeft;
    private double? _captureToolbarTop;
    private bool _textBold;
    private bool _textItalic;
    private bool _textUnderline;
    private bool _textStrikethrough;
    private TextAlignment _textAlignment = TextAlignment.Left;
    private bool _textOutline;
    private FrameworkElement? _customToolCursor;
    private bool _customToolCursorCentered;
    private bool _surfacePointerInside;
    private string _selectedColor = "#FFEF4444";
    private readonly Dictionary<string, double> _toolSizes = new(StringComparer.OrdinalIgnoreCase);

    internal event Action<AnnotationAppliedEventArgs>? Applied;
    internal event Action<AnnotationAppliedEventArgs>? DocumentStored;
    public event EventHandler? Cancelled;

    public AnnotationEditorControl()
    {
        _settings = SettingsService.Load();
        ToolbarThemeService.ApplyTo(Resources, _settings.ToolbarSizeMode);
        _toolSizes["Shape"] = _settings.AnnotationShapeSize;
        _toolSizes["Arrow"] = _settings.AnnotationArrowSize;
        _toolSizes["Pencil"] = _settings.AnnotationPencilSize;
        _toolSizes["Marker"] = _settings.AnnotationMarkerSize;
        _toolSizes["Blur"] = _settings.AnnotationBlurSize;
        _toolSizes["Text"] = _settings.AnnotationTextSize;
        _toolSizes["Eraser"] = _settings.AnnotationEraserSize;
        InitializeComponent();
        ApplyToolbarConfiguration(_settings.AnnotationToolbarOrder, _settings.AnnotationToolbarEnabled);
        LoadStyleSettings();
        Loaded += (_, _) => Focus();
        SizeChanged += (_, _) => { if (_captureOverlayMode) LayoutCaptureOverlay(); };
    }

    internal void LoadImage(BitmapSource source, IEnumerable<AnnotationItem>? items = null)
    {
        _source = source;
        _blurredSource = null;
        _items = items?.Select(item => item.Clone()).ToList() ?? [];
        _undo.Clear();
        _redo.Clear();
        _selectedId = null;
        _activeId = null;
        _nextNumber = Math.Max(1, _items.Where(item => item.Kind == AnnotationKind.Number).Select(item => int.TryParse(item.Text, out var value) ? value + 1 : 1).DefaultIfEmpty(1).Max());
        BackgroundImage.Source = source;
        Surface.Width = Math.Max(1, source.PixelWidth);
        Surface.Height = Math.Max(1, source.PixelHeight);
        RenderAnnotations();
        if (FindToolButton(_tool) is not { Visibility: Visibility.Visible } currentButton && FirstVisibleToolButton() is { } firstButton)
            ActivateTool(firstButton.Tag as string ?? "Rectangle", firstButton);
        Focus();
    }

    internal void ApplyToolbarConfiguration(IEnumerable<string>? order, IEnumerable<string>? enabled)
    {
        var normalizedOrder = AnnotationToolbarCatalog.NormalizeOrder(order);
        var normalizedEnabled = AnnotationToolbarCatalog.NormalizeEnabled(enabled).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var buttons = ToolPanel.Children.OfType<Button>()
            .Where(button => button.Tag is string)
            .ToDictionary(button => (string)button.Tag, StringComparer.OrdinalIgnoreCase);

        foreach (var button in buttons.Values.ToList()) ToolPanel.Children.Remove(button);
        foreach (var key in normalizedOrder)
        {
            if (!buttons.TryGetValue(key, out var button)) continue;
            button.Visibility = normalizedEnabled.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
            ToolPanel.Children.Add(button);
        }
    }

    internal void ActivateConfiguredTool(string tool)
    {
        if (FindToolButton(tool) is { Visibility: Visibility.Visible } button)
            ActivateTool(tool, button);
    }

    private Button? FindToolButton(string tool) => ToolPanel.Children.OfType<Button>()
        .FirstOrDefault(button => button.Tag is string tag && tag.Equals(tool, StringComparison.OrdinalIgnoreCase));

    private Button? FirstVisibleToolButton() => ToolPanel.Children.OfType<Button>()
        .FirstOrDefault(button => button.Visibility == Visibility.Visible && button.Tag is string);

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        CommitTextEditor();
        if (sender is not Button { Tag: string tool } activeButton) return;
        ActivateTool(tool, activeButton);
    }

    private void PaletteColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color }) return;
        SetSelectedColor(color);
        ApplyTextEditorStyle();
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void SelectedColorPreview_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        SetSelectedColor($"#FF{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}");
        ApplyTextEditorStyle();
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void SetSelectedColor(string color)
    {
        _selectedColor = color;
        var wasLoading = _loadingStyle;
        _loadingStyle = true;
        ColorBox.SelectedValue = color;
        _loadingStyle = wasLoading;
        SelectedColorFill.Background = BrushFrom(color);
    }

    private void SizeCycle_Click(object sender, RoutedEventArgs e)
    {
        double[] sizes = _tool is "Eraser" or "Blur"
            ? [8, 12, 18, 24, 32, 48, 64, 96]
            : [1, 2, 4, 6, 10, 16, 21, 24];
        var index = Array.FindIndex(sizes, size => size > ThicknessSlider.Value + 0.1);
        ThicknessSlider.Value = index < 0 ? sizes[0] : sizes[index];
        UpdateToolSizeLabels();
    }

    private void UpdateToolSizeLabels()
    {
        var label = Math.Round(ThicknessSlider.Value).ToString();
        ShapeSizeButton.Content = ThicknessSlider.Value <= 4 ? "•" : label;
        ArrowSizeButton.Content = ThicknessSlider.Value <= 4 ? "•" : label;
        PencilSizeButton.Content = ThicknessSlider.Value <= 4 ? "•" : label;
        MarkerSizeButton.Content = label;
        BlurSizeButton.Content = label;
        EraserSizeButton.Content = label;
        BasicSizeButton.Content = label;
    }

    private void TextStyle_Click(object sender, RoutedEventArgs e)
    {
        if (sender == TextBoldButton) _textBold = !_textBold;
        else if (sender == TextItalicButton) _textItalic = !_textItalic;
        else if (sender == TextUnderlineButton) _textUnderline = !_textUnderline;
        else if (sender == TextStrikeButton) _textStrikethrough = !_textStrikethrough;
        else if (sender == TextAlignButton) _textAlignment = _textAlignment switch
        {
            TextAlignment.Left => TextAlignment.Center,
            TextAlignment.Center => TextAlignment.Right,
            _ => TextAlignment.Left
        };
        else if (sender == TextOutlineButton) _textOutline = !_textOutline;
        UpdateStyleButtons();
        ApplyTextEditorStyle();
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void TextFont_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStyle) return;
        ApplyTextEditorStyle();
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void TextSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStyle || !double.TryParse(TextSizeBox.SelectedValue as string, out var value)) return;
        ThicknessSlider.Value = Math.Clamp(value / 4.0, 1, 24);
        ApplyTextEditorStyle();
        UpdateToolSizeLabels();
    }

    private void ActivateTool(string tool, Button activeButton)
    {
        RememberCurrentToolSize();
        foreach (var button in ToolPanel.Children.OfType<Button>())
        {
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(16, 24, 40));
        }
        activeButton.Background = new SolidColorBrush(Color.FromRgb(47, 128, 237));
        activeButton.Foreground = Brushes.White;
        _tool = tool;
        var wasLoading = _loadingStyle;
        _loadingStyle = true;
        ThicknessSlider.Maximum = tool is "Eraser" or "Blur" ? 96 : 24;
        ThicknessSlider.Value = Math.Clamp(_toolSizes.GetValueOrDefault(ToolSizeKey(tool), 4), 1, ThicknessSlider.Maximum);
        if (tool is "Text" or "Callout")
        {
            TextSizeBox.SelectedValue = Math.Clamp((int)Math.Round(ThicknessSlider.Value * 4), 9, 72).ToString();
            if (TextSizeBox.SelectedIndex < 0) TextSizeBox.SelectedValue = "16";
        }
        _loadingStyle = wasLoading;
        _selectedId = null;
        StatusText.Text = tool switch
        {
            "Select" => L("Click an annotation to select it, then drag to move it."),
            "Text" => L("Click to place text. Enter adds a line; Ctrl+Enter finishes."),
            "Callout" => L("Click the image, type the callout, then press Ctrl+Enter."),
            "Number" => L("Click the image to add the next numbered marker."),
            "Eraser" => L("Drag the eraser over only the pixels you want to remove."),
            "Blur" => L("Drag to paint blur directly where the brush passes."),
            _ => LocalizationService.Format("Drag on the image to add {0}.", L(tool))
        };
        UpdateSurfaceCursor(tool);
        UpdateToolOptions(tool);
        UpdateToolSizeLabels();
        RenderAnnotations();
    }

    private void UpdateSurfaceCursor(string tool)
    {
        ToolCursorLayer.Children.Clear();
        _customToolCursor = null;
        _customToolCursorCentered = false;
        Surface.Cursor = tool switch
        {
            "Select" => Cursors.Arrow,
            "Text" => Cursors.IBeam,
            "Pencil" or "Marker" => Cursors.Pen,
            "Eraser" or "Blur" => Cursors.None,
            _ => Cursors.Cross
        };

        if (tool == "Blur")
        {
            var size = Math.Max(7, ThicknessSlider.Value);
            var brushPreview = new Shapes.Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(Color.FromArgb(44, 71, 84, 103)),
                Stroke = Brushes.White,
                StrokeThickness = Math.Max(1, 1.5 / Math.Max(0.25, size / 21)),
                IsHitTestVisible = false,
                Visibility = _surfacePointerInside ? Visibility.Visible : Visibility.Collapsed
            };
            _customToolCursor = brushPreview;
            _customToolCursorCentered = true;
            ToolCursorLayer.Children.Add(brushPreview);
            return;
        }

        if (tool != "Eraser") return;
        var eraserSize = Math.Max(8, ThicknessSlider.Value);
        var eraser = new Border
        {
            Width = eraserSize,
            Height = eraserSize,
            IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.FromArgb(38, 248, 113, 113)),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(2),
            Visibility = _surfacePointerInside ? Visibility.Visible : Visibility.Collapsed
        };
        eraser.Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 1, ShadowDepth = 0, Opacity = 0.9 };
        _customToolCursor = eraser;
        _customToolCursorCentered = true;
        ToolCursorLayer.Children.Add(eraser);
    }

    private void Surface_MouseEnter(object sender, MouseEventArgs e)
    {
        _surfacePointerInside = true;
        if (_customToolCursor is not null) _customToolCursor.Visibility = Visibility.Visible;
    }

    private void Surface_MouseLeave(object sender, MouseEventArgs e)
    {
        _surfacePointerInside = false;
        if (_customToolCursor is not null) _customToolCursor.Visibility = Visibility.Collapsed;
    }

    private void PositionCustomToolCursor(Point point, bool visible)
    {
        if (_customToolCursor is null) return;
        _customToolCursor.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var width = _customToolCursor.Width;
        var height = _customToolCursor.Height;
        var left = _customToolCursorCentered ? point.X - width / 2 : point.X + 5;
        var top = _customToolCursorCentered ? point.Y - height / 2 : point.Y - 20;
        Canvas.SetLeft(_customToolCursor, Math.Clamp(left, 0, Math.Max(0, Surface.Width - width)));
        Canvas.SetTop(_customToolCursor, Math.Clamp(top, 0, Math.Max(0, Surface.Height - height)));
    }

    private static string ToolSizeKey(string tool) => tool switch
    {
        "Rectangle" or "Ellipse" or "Number" or "Callout" or "Magnify" => "Shape",
        "Arrow" or "Line" => "Arrow",
        _ => tool
    };

    private void RememberCurrentToolSize()
    {
        var key = ToolSizeKey(_tool);
        if (_toolSizes.ContainsKey(key)) _toolSizes[key] = ThicknessSlider.Value;
    }

    private void UpdateToolOptions(string tool)
    {
        ShapeOptionsPanel.Visibility = tool is "Rectangle" or "Ellipse" ? Visibility.Visible : Visibility.Collapsed;
        ArrowOptionsPanel.Visibility = tool is "Arrow" or "Line" ? Visibility.Visible : Visibility.Collapsed;
        PencilOptionsPanel.Visibility = tool == "Pencil" ? Visibility.Visible : Visibility.Collapsed;
        MarkerOptionsPanel.Visibility = tool == "Marker" ? Visibility.Visible : Visibility.Collapsed;
        BlurOptionsPanel.Visibility = tool == "Blur" ? Visibility.Visible : Visibility.Collapsed;
        BasicOptionsPanel.Visibility = tool is "Number" or "Magnify" ? Visibility.Visible : Visibility.Collapsed;
        TextOptionsPanel.Visibility = tool is "Text" or "Callout" ? Visibility.Visible : Visibility.Collapsed;
        EraserOptionsPanel.Visibility = tool == "Eraser" ? Visibility.Visible : Visibility.Collapsed;
        ColorPalettePanel.Visibility = tool is "Rectangle" or "Ellipse" or "Arrow" or "Line" or "Pencil" or "Marker" or "Text" or "Callout" or "Number" or "Magnify"
            ? Visibility.Visible : Visibility.Collapsed;
        PropertiesToolbarFrame.Visibility = tool == "Select" ? Visibility.Collapsed : Visibility.Visible;
        UpdateStyleButtons();
        if (_captureOverlayMode) Dispatcher.BeginInvoke(LayoutCaptureOverlay);
    }

    internal void ConfigureCaptureOverlay(Rect surfaceBounds, Size viewport, Rect? toolbarAnchor = null, bool showActions = false,
        double? toolbarLeft = null, double? toolbarTop = null, bool showPrimaryToolbar = true)
    {
        _captureOverlayMode = true;
        _captureSurfaceBounds = surfaceBounds;
        _captureToolbarAnchor = toolbarAnchor ?? surfaceBounds;
        _captureViewport = viewport;
        _captureToolbarLeft = toolbarLeft;
        _captureToolbarTop = toolbarTop;
        Width = Math.Max(1, viewport.Width);
        Height = Math.Max(1, viewport.Height);
        ActionSeparator.Visibility = showActions ? Visibility.Visible : Visibility.Collapsed;
        CopyActionButton.Visibility = showActions ? Visibility.Visible : Visibility.Collapsed;
        SaveActionButton.Visibility = showActions ? Visibility.Visible : Visibility.Collapsed;
        ApplyActionButton.Visibility = showActions ? Visibility.Visible : Visibility.Collapsed;
        CancelActionButton.Visibility = Visibility.Collapsed;
        PrimaryToolbarFrame.Visibility = showPrimaryToolbar ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRowSpan(SurfaceHost, 2);
        SurfaceHost.BorderThickness = new Thickness(0);
        SurfaceHost.HorizontalAlignment = HorizontalAlignment.Left;
        SurfaceHost.VerticalAlignment = VerticalAlignment.Top;
        if (FirstVisibleToolButton() is { } firstButton)
            ActivateTool(firstButton.Tag as string ?? "Rectangle", firstButton);
        Dispatcher.BeginInvoke(LayoutCaptureOverlay);
    }

    internal void UpdateCaptureToolbarPosition(double left, double top)
    {
        if (!_captureOverlayMode) return;
        _captureToolbarLeft = left;
        _captureToolbarTop = top;
        Dispatcher.BeginInvoke(LayoutCaptureOverlay);
    }

    internal void SetCapturePropertiesToolbarVisible(bool visible)
    {
        if (!_captureOverlayMode) return;
        PropertiesToolbarFrame.Visibility = visible && _tool != "Select"
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (visible) Dispatcher.BeginInvoke(LayoutCaptureOverlay);
    }

    internal void UndoForCapture() => Undo();
    internal void RedoForCapture() => Redo();

    internal void UpdateCaptureOverlayBounds(Rect surfaceBounds, Size viewport, Rect? toolbarAnchor = null)
    {
        if (!_captureOverlayMode) return;
        _captureSurfaceBounds = surfaceBounds;
        if (toolbarAnchor is { } anchor) _captureToolbarAnchor = anchor;
        _captureViewport = viewport;
        Width = Math.Max(1, viewport.Width);
        Height = Math.Max(1, viewport.Height);
        Dispatcher.BeginInvoke(LayoutCaptureOverlay);
    }

    private void LayoutCaptureOverlay()
    {
        if (!_captureOverlayMode || _captureViewport.Width <= 1 || _captureViewport.Height <= 1) return;
        var bounds = Rect.Intersect(new Rect(new Point(), _captureViewport), _captureSurfaceBounds);
        if (bounds.IsEmpty) return;
        SurfaceHost.Width = Math.Max(1, bounds.Width);
        SurfaceHost.Height = Math.Max(1, bounds.Height);
        SurfaceHost.Margin = new Thickness(bounds.X, bounds.Y, 0, 0);

        var availableWidth = Math.Max(240, _captureViewport.Width - 16);
        // Keep every annotation command in the primary row. The row below is
        // reserved exclusively for properties of the selected tool.
        ToolbarHost.Width = double.NaN;
        ToolbarHost.Measure(new Size(availableWidth, double.PositiveInfinity));
        var toolbarWidth = Math.Min(availableWidth, Math.Ceiling(ToolbarHost.DesiredSize.Width));
        ToolbarHost.Width = toolbarWidth;
        ToolbarHost.HorizontalAlignment = HorizontalAlignment.Left;
        ToolbarHost.Margin = new Thickness(0);
        ToolbarHost.RenderTransform = Transform.Identity;
        ToolbarHost.Measure(new Size(toolbarWidth, double.PositiveInfinity));
        UpdateLayout();
        var toolbarHeight = Math.Max(1, ToolbarHost.ActualHeight);
        var anchor = _captureToolbarAnchor.IsEmpty ? bounds : _captureToolbarAnchor;
        var placement = OverlayLayoutService.PlaceBelowAndKeepVisible(anchor, new Size(toolbarWidth, toolbarHeight), _captureViewport, 5);
        var maximumLeft = Math.Max(5, _captureViewport.Width - toolbarWidth - 5);
        var positioningWidth = Math.Min(availableWidth, Math.Max(toolbarWidth, 460));
        var initialLeft = OverlayLayoutService.PlaceBelowAndKeepVisible(
            anchor, new Size(positioningWidth, toolbarHeight), _captureViewport, 5).X;
        var stableLeft = Math.Clamp(_captureToolbarLeft ?? initialLeft, 5, maximumLeft);
        var maximumTop = Math.Max(5, _captureViewport.Height - toolbarHeight - 5);
        var stableTop = Math.Clamp(_captureToolbarTop ?? placement.Y, 5, maximumTop);
        _captureToolbarLeft = stableLeft;
        _captureToolbarTop = stableTop;
        ToolbarHost.Margin = new Thickness(0);
        UpdateLayout();
        var naturalPosition = ToolbarHost.TranslatePoint(new Point(0, 0), this);
        ToolbarHost.RenderTransform = new TranslateTransform(
            stableLeft - naturalPosition.X,
            stableTop - naturalPosition.Y);
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_textEditor is not null)
        {
            if (_textEditorFrame is not null && e.OriginalSource is DependencyObject source &&
                (ReferenceEquals(source, _textEditorFrame) || _textEditorFrame.IsAncestorOf(source)))
                return;
            CommitTextEditor();
        }
        var point = Clamp(e.GetPosition(AnnotationCanvas));
        _start = _last = point;

        // Paint-style transform handles stay usable while the drawing tool
        // remains active; a separate selection tool is not required.
        if (FindHandle(e.OriginalSource as DependencyObject) is { } activeHandle)
        {
            var target = _items.FirstOrDefault(item => item.Id == activeHandle.ItemId);
            if (target is null) return;
            PushUndo();
            _selectedId = _activeId = target.Id;
            _transformMode = activeHandle.Handle;
            _transformStart = point;
            _transformOriginal = target.Clone();
            _dragging = true;
            Surface.CaptureMouse();
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            FindItemId(e.OriginalSource as DependencyObject) is Guid directSelection)
        {
            _selectedId = directSelection;
            if (_items.FirstOrDefault(item => item.Id == directSelection) is { } selected)
                SyncStyleControls(selected);
            RenderAnnotations();
            e.Handled = true;
            return;
        }

        if (_tool == "Select")
        {
            _selectedId = FindItemId(e.OriginalSource as DependencyObject);
            if (e.ClickCount == 2 && _selectedId is Guid editId && _items.FirstOrDefault(item => item.Id == editId) is { Kind: AnnotationKind.Text or AnnotationKind.Callout } editableText)
            {
                PushUndo();
                _pendingTextKind = editableText.Kind;
                BeginTextEditor(new Point(editableText.X, editableText.Y), editableText);
                e.Handled = true;
                return;
            }
            if (_selectedId is not null)
            {
                PushUndo();
                _activeId = _selectedId;
                _dragging = true;
                Surface.CaptureMouse();
                if (_items.FirstOrDefault(item => item.Id == _selectedId) is { } selected) SyncStyleControls(selected);
            }
            RenderAnnotations();
            return;
        }

        if (_tool is "Eraser" or "Blur")
        {
            AddBrushStroke(point);
            _dragging = true;
            Surface.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (_tool is "Text" or "Callout")
        {
            _pendingTextKind = _tool == "Callout" ? AnnotationKind.Callout : AnnotationKind.Text;
            StartTextEditorDrag(point);
            e.Handled = true;
            return;
        }

        if (_tool == "Number")
        {
            PushUndo();
            var marker = new AnnotationItem
            {
                Kind = AnnotationKind.Number,
                X = Math.Clamp(point.X - 23, 0, Math.Max(0, Surface.Width - 46)),
                Y = Math.Clamp(point.Y - 23, 0, Math.Max(0, Surface.Height - 46)),
                Width = 46,
                Height = 46,
                Text = (_nextNumber++).ToString(),
                Color = SelectedColor(),
                Thickness = ThicknessSlider.Value
            };
            ApplyCurrentStyle(marker);
            marker.FillOpacity = Math.Max(marker.FillOpacity, 1);
            _items.Add(marker);
            _selectedId = marker.Id;
            RenderAnnotations();
            e.Handled = true;
            return;
        }

        if (!Enum.TryParse<AnnotationKind>(_tool, out var kind)) return;
        PushUndo();
        var item = new AnnotationItem
        {
            Kind = kind,
            Color = SelectedColor(),
            Thickness = ThicknessSlider.Value,
            X = point.X,
            Y = point.Y,
            Points = kind is AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Pencil or AnnotationKind.Marker
                ? new List<Point> { point, point }
                : new List<Point>()
        };
        ApplyCurrentStyle(item);
        _items.Add(item);
        _activeId = item.Id;
        _selectedId = item.Id;
        _dragging = true;
        Surface.CaptureMouse();
        RenderAnnotations();
        e.Handled = true;
    }

    private void Surface_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var id = FindItemId(e.OriginalSource as DependencyObject);
        if (id is null) return;
        _selectedId = id;
        RenderAnnotations();
        var menu = new ContextMenu();
        AddAnnotationMenuItem(menu, "Bring to front", () => ReorderSelected(int.MaxValue));
        AddAnnotationMenuItem(menu, "Bring forward", () => ReorderSelected(1));
        AddAnnotationMenuItem(menu, "Send backward", () => ReorderSelected(-1));
        AddAnnotationMenuItem(menu, "Send to back", () => ReorderSelected(int.MinValue));
        menu.Items.Add(new Separator());
        AddAnnotationMenuItem(menu, "Duplicate", DuplicateSelected);
        AddAnnotationMenuItem(menu, "Delete", DeleteSelected);
        menu.PlacementTarget = Surface;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void AddAnnotationMenuItem(ContextMenu menu, string label, Action action)
    {
        var item = new MenuItem { Header = L(label) };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static string L(string value) => LocalizationService.Current(value);

    private void DeleteSelected()
    {
        if (_selectedId is not Guid id) return;
        PushUndo();
        _items.RemoveAll(item => item.Id == id);
        _selectedId = null;
        RenderAnnotations();
    }

    private void MoveSelected(double dx, double dy)
    {
        if (_selectedId is not Guid id || _items.FirstOrDefault(item => item.Id == id) is not { } item) return;
        PushUndo();
        item.X += dx;
        item.Y += dy;
        if (item.Points.Count > 0)
            item.Points = item.Points.Select(point => new Point(point.X + dx, point.Y + dy)).ToList();
        RenderAnnotations();
    }

    private void ReorderSelected(int delta)
    {
        if (_selectedId is not Guid id) return;
        var index = _items.FindIndex(item => item.Id == id);
        if (index < 0) return;
        var next = delta == int.MaxValue ? _items.Count - 1 : delta == int.MinValue ? 0 : Math.Clamp(index + delta, 0, _items.Count - 1);
        if (next == index) return;
        PushUndo();
        var item = _items[index];
        _items.RemoveAt(index);
        _items.Insert(next, item);
        RenderAnnotations();
    }

    private void DuplicateSelected()
    {
        if (_selectedId is not Guid id || _items.FirstOrDefault(item => item.Id == id) is not { } source) return;
        PushUndo();
        var copy = new AnnotationItem
        {
            Kind = source.Kind, Points = source.Points.Select(point => new Point(point.X + 8, point.Y + 8)).ToList(),
            X = source.X + 8, Y = source.Y + 8, Width = source.Width, Height = source.Height,
            Color = source.Color, Thickness = source.Thickness, Text = source.Text, Opacity = source.Opacity,
            FillOpacity = source.FillOpacity, Dashed = source.Dashed, ArrowHeads = source.ArrowHeads,
            EffectShape = source.EffectShape, Rotation = source.Rotation, FontFamily = source.FontFamily,
            Bold = source.Bold, Italic = source.Italic, Underline = source.Underline,
            Strikethrough = source.Strikethrough, TextAlignment = source.TextAlignment, Outline = source.Outline
        };
        _items.Add(copy);
        _selectedId = copy.Id;
        RenderAnnotations();
    }

    private AnnotationItem AddBrushStroke(Point point)
    {
        PushUndo();
        var brushStroke = new AnnotationItem
        {
            Kind = _tool == "Eraser" ? AnnotationKind.Eraser : AnnotationKind.Blur,
            Thickness = ThicknessSlider.Value,
            Points = [point]
        };
        ApplyCurrentStyle(brushStroke);
        _items.Add(brushStroke);
        _activeId = brushStroke.Id;
        _selectedId = null;
        RenderAnnotations();
        return brushStroke;
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        var pointer = Clamp(e.GetPosition(AnnotationCanvas));
        if (_textBoxDragging)
        {
            UpdateTextEditorDrag(pointer);
            e.Handled = true;
            return;
        }
        PositionCustomToolCursor(pointer, FindHandle(e.OriginalSource as DependencyObject) is null);
        if (!_dragging || _activeId is null) return;
        var point = pointer;
        var item = _items.FirstOrDefault(candidate => candidate.Id == _activeId);
        if (item is null) return;

        if (!string.IsNullOrWhiteSpace(_transformMode) && _transformOriginal is not null)
        {
            TransformItem(item, point);
        }
        else if (_tool == "Select")
        {
            MoveItem(item, point.X - _last.X, point.Y - _last.Y);
        }
        else if (item.Kind is AnnotationKind.Eraser || item.Kind == AnnotationKind.Blur && item.Points.Count > 0)
        {
            if (Distance(_last, point) < Math.Max(1.25, item.Thickness * 0.08)) return;
            item.Points.Add(point);
            _last = point;
            UpdateLiveBrushVisual(item);
            return;
        }
        else if (item.Kind is AnnotationKind.Pencil or AnnotationKind.Marker)
        {
            if (Distance(_last, point) < 1.5) return;
            item.Points.Add(point);
            _last = point;
            RequestRender();
            return;
        }
        else if (item.Kind is AnnotationKind.Line or AnnotationKind.Arrow)
        {
            if (item.Points.Count < 2) item.Points.Add(point); else item.Points[1] = point;
        }
        else
        {
            item.X = Math.Min(_start.X, point.X);
            item.Y = Math.Min(_start.Y, point.Y);
            item.Width = Math.Abs(point.X - _start.X);
            item.Height = Math.Abs(point.Y - _start.Y);
        }

        _last = point;
        RequestRender();
    }

    private void UpdateLiveBrushVisual(AnnotationItem item)
    {
        if (item.Kind is AnnotationKind.Eraser or AnnotationKind.Blur &&
            _renderedElements.TryGetValue(item.Id, out var element) && element is Shapes.Path path)
            path.Data = BrushStrokeGeometry(item);
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_textBoxDragging)
        {
            FinishTextEditorDrag();
            e.Handled = true;
            return;
        }
        if (!_dragging) return;
        var completedId = _activeId;
        _dragging = false;
        _activeId = null;
        _transformMode = string.Empty;
        _transformOriginal = null;
        Surface.ReleaseMouseCapture();
        _items.RemoveAll(item => !IsStrokeItem(item) &&
            item.Kind is not (AnnotationKind.Text or AnnotationKind.Number or AnnotationKind.Callout) &&
            (item.Width < 2 || item.Height < 2));
        _selectedId = completedId is Guid id && _items.FirstOrDefault(item => item.Id == id) is { Kind: not (AnnotationKind.Eraser or AnnotationKind.Blur) }
            ? id
            : null;
        RenderAnnotations();
    }

    private static bool IsStrokeItem(AnnotationItem item) =>
        item.Kind is AnnotationKind.Pencil or AnnotationKind.Marker or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Eraser ||
        item.Kind == AnnotationKind.Blur && item.Points.Count > 0;

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

    private void RenderAnnotations()
    {
        _renderPending = false;
        if (_textEditor is not null) return;
        AnnotationCanvas.Children.Clear();
        _renderedElements.Clear();
        foreach (var item in _items)
        {
            var element = CreateElement(item);
            if (element is null) continue;
            AnnotationCanvas.Children.Add(element);
            _renderedElements[item.Id] = element;
        }

        if (!_dragging && _selectedId is Guid id && _items.FirstOrDefault(item => item.Id == id) is { } selected)
        {
            var bounds = Bounds(selected);
            AddSelectionHandles(selected, bounds);
        }
    }

    private void RequestRender()
    {
        if (_renderPending) return;
        _renderPending = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(RenderAnnotations));
    }

    private void AddSelectionHandles(AnnotationItem item, Rect bounds)
    {
        var centerX = bounds.X + bounds.Width / 2;
        var centerY = bounds.Y + bounds.Height / 2;
        AddHandle(item.Id, "NW", bounds.Left, bounds.Top, Cursors.SizeNWSE);
        AddHandle(item.Id, "N", centerX, bounds.Top, Cursors.SizeNS);
        AddHandle(item.Id, "NE", bounds.Right, bounds.Top, Cursors.SizeNESW);
        AddHandle(item.Id, "E", bounds.Right, centerY, Cursors.SizeWE);
        AddHandle(item.Id, "SE", bounds.Right, bounds.Bottom, Cursors.SizeNWSE);
        AddHandle(item.Id, "S", centerX, bounds.Bottom, Cursors.SizeNS);
        AddHandle(item.Id, "SW", bounds.Left, bounds.Bottom, Cursors.SizeNESW);
        AddHandle(item.Id, "W", bounds.Left, centerY, Cursors.SizeWE);
    }

    private void AddHandle(Guid itemId, string handle, double x, double y, Cursor cursor)
    {
        const double size = 7;
        var grip = new Shapes.Rectangle
        {
            Width = size,
            Height = size,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(17, 24, 39)),
            StrokeThickness = 1,
            Cursor = cursor,
            Tag = new AnnotationHandleTag(itemId, handle),
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(grip, x - size / 2);
        Canvas.SetTop(grip, y - size / 2);
        Panel.SetZIndex(grip, 1000);
        AnnotationCanvas.Children.Add(grip);
    }

    private FrameworkElement? CreateElement(AnnotationItem item)
    {
        var brush = BrushFrom(item.Color);
        FrameworkElement element;
        switch (item.Kind)
        {
            case AnnotationKind.Rectangle:
                element = new Shapes.Rectangle { Stroke = brush, StrokeThickness = item.Thickness, Fill = FillBrush(item), StrokeDashArray = DashArray(item) };
                Place(element, item);
                break;
            case AnnotationKind.Ellipse:
                element = new Shapes.Ellipse { Stroke = brush, StrokeThickness = item.Thickness, Fill = FillBrush(item), StrokeDashArray = DashArray(item) };
                Place(element, item);
                break;
            case AnnotationKind.Line:
                element = StrokePolyline(item, brush, false);
                break;
            case AnnotationKind.Arrow:
                element = StrokePolyline(item, brush, true);
                break;
            case AnnotationKind.Pencil:
                element = StrokePolyline(item, brush, false);
                break;
            case AnnotationKind.Marker:
                element = StrokePolyline(item, brush, false);
                element.Opacity = 0.38;
                break;
            case AnnotationKind.Text:
                var textBlock = new TextBlock
                {
                    Text = item.Text,
                    Foreground = brush,
                    FontSize = Math.Max(9, item.Thickness * 4),
                    FontFamily = new FontFamily(item.FontFamily),
                    FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal,
                    TextDecorations = TextDecorationsFor(item.Underline, item.Strikethrough),
                    TextAlignment = Enum.TryParse<TextAlignment>(item.TextAlignment, out var alignment) ? alignment : TextAlignment.Left,
                    TextWrapping = TextWrapping.Wrap,
                    Width = Math.Max(40, item.Width),
                    Height = Math.Max(24, item.Height),
                    Padding = new Thickness(5, 3, 5, 3),
                    Background = Brushes.Transparent
                };
                if (item.Outline)
                    textBlock.Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 1.5, ShadowDepth = 0, Opacity = 1 };
                element = textBlock;
                element.Tag = item.Id;
                Canvas.SetLeft(element, item.X);
                Canvas.SetTop(element, item.Y);
                break;
            case AnnotationKind.Number:
                element = new Border
                {
                    CornerRadius = new CornerRadius(Math.Max(4, item.Width / 2)),
                    Background = brush,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(Math.Max(1, item.Thickness / 2)),
                    Child = new TextBlock { Text = item.Text, Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = Math.Max(15, item.Height * 0.46), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                };
                Place(element, item);
                break;
            case AnnotationKind.Callout:
                element = CalloutElement(item, brush);
                Place(element, item);
                break;
            case AnnotationKind.Mosaic:
                element = EffectElement(item, pixelate: true);
                break;
            case AnnotationKind.Blur:
                element = item.Points.Count > 0 ? BlurBrushElement(item) : EffectElement(item, pixelate: false);
                break;
            case AnnotationKind.Magnify:
                element = MagnifyElement(item);
                break;
            case AnnotationKind.Eraser:
                element = EraserBrushElement(item);
                break;
            default:
                return null;
        }
        element.Tag = item.Id;
        element.Opacity = Math.Clamp(item.Kind == AnnotationKind.Marker ? item.Opacity * 0.38 : item.Opacity, 0.12, 1);
        if (Math.Abs(item.Rotation) > 0.01)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new RotateTransform(item.Rotation);
        }
        return element;
    }

    private FrameworkElement BlurBrushElement(AnnotationItem item)
    {
        return BrushImagePath(EnsureBlurredSource(), item, BitmapScalingMode.HighQuality);
    }

    private FrameworkElement EraserBrushElement(AnnotationItem item)
    {
        return BrushImagePath(_source, item, BitmapScalingMode.NearestNeighbor);
    }

    private FrameworkElement BrushImagePath(BitmapSource source, AnnotationItem item, BitmapScalingMode scalingMode)
    {
        var surfaceBounds = new Rect(0, 0, Math.Max(1, _source.PixelWidth), Math.Max(1, _source.PixelHeight));
        var sourceBrush = new ImageBrush(source)
        {
            Stretch = Stretch.Fill,
            Viewbox = surfaceBounds,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = surfaceBounds,
            ViewportUnits = BrushMappingMode.Absolute,
            TileMode = TileMode.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top
        };
        var path = new Shapes.Path
        {
            Data = BrushStrokeGeometry(item),
            Fill = Brushes.Transparent,
            Stroke = sourceBrush,
            StrokeThickness = Math.Max(3, item.Thickness),
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
            Tag = item.Id
        };
        RenderOptions.SetBitmapScalingMode(path, scalingMode);
        return path;
    }

    private BitmapSource EnsureBlurredSource()
    {
        if (_blurredSource is not null) return _blurredSource;
        var width = Math.Max(1, _source.PixelWidth);
        var height = Math.Max(1, _source.PixelHeight);
        var image = new Image
        {
            Source = _source,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            Effect = new BlurEffect
            {
                Radius = 12,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality
            }
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.Measure(new Size(width, height));
        image.Arrange(new Rect(0, 0, width, height));
        image.UpdateLayout();
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(image);
        result.Freeze();
        _blurredSource = result;
        return result;
    }

    private static Geometry BrushStrokeGeometry(AnnotationItem item)
    {
        var points = item.Points.Count > 0 ? item.Points : [new Point(item.X, item.Y)];
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], isFilled: false, isClosed: false);
            if (points.Count == 1)
                context.LineTo(new Point(points[0].X + 0.01, points[0].Y), isStroked: true, isSmoothJoin: true);
            else
                context.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        return geometry;
    }

    private FrameworkElement StrokePolyline(AnnotationItem item, Brush brush, bool arrow)
    {
        var points = item.Points.Count > 0 ? item.Points.ToList() : [new Point(item.X, item.Y)];
        if (arrow && points.Count >= 2) return ArrowElement(item, brush, points[0], points[^1]);
        var line = new Shapes.Polyline
        {
            Points = new PointCollection(points),
            Stroke = brush,
            StrokeThickness = item.Kind == AnnotationKind.Marker ? Math.Max(8, item.Thickness) : item.Thickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            StrokeDashArray = DashArray(item),
            Tag = item.Id
        };
        return line;
    }

    private FrameworkElement ArrowElement(AnnotationItem item, Brush brush, Point start, Point end)
    {
        var geometry = new GeometryGroup();
        geometry.Children.Add(new LineGeometry(start, end));
        if (item.ArrowHeads is "End" or "Both") AddArrowHead(geometry, start, end, item.Thickness);
        if (item.ArrowHeads == "Both") AddArrowHead(geometry, end, start, item.Thickness);
        return new Shapes.Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = item.Thickness,
            StrokeDashArray = DashArray(item),
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Fill = Brushes.Transparent,
            Tag = item.Id
        };
    }

    private static void AddArrowHead(GeometryGroup geometry, Point start, Point end, double thickness)
    {
        var angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        var length = Math.Max(12, thickness * 4);
        var left = new Point(end.X - length * Math.Cos(angle - Math.PI / 6), end.Y - length * Math.Sin(angle - Math.PI / 6));
        var right = new Point(end.X - length * Math.Cos(angle + Math.PI / 6), end.Y - length * Math.Sin(angle + Math.PI / 6));
        geometry.Children.Add(new LineGeometry(end, left));
        geometry.Children.Add(new LineGeometry(end, right));
    }

    private FrameworkElement CalloutElement(AnnotationItem item, Brush brush)
    {
        var grid = new Grid();
        var bubble = new Border
        {
            Margin = new Thickness(0, 0, 0, 14),
            CornerRadius = new CornerRadius(10),
            Background = item.FillOpacity > 0 ? FillBrush(item) : new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = brush,
            BorderThickness = new Thickness(item.Thickness),
            Child = new TextBlock
            {
                Text = item.Text,
                Foreground = brush,
                FontSize = Math.Max(9, item.Thickness * 4),
                FontFamily = new FontFamily(item.FontFamily),
                FontWeight = item.Bold ? FontWeights.Bold : FontWeights.SemiBold,
                FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = TextDecorationsFor(item.Underline, item.Strikethrough),
                TextAlignment = Enum.TryParse<TextAlignment>(item.TextAlignment, out var alignment) ? alignment : TextAlignment.Left,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(10, 7, 10, 7)
            }
        };
        var tail = new Shapes.Polygon
        {
            Points = new PointCollection([new Point(24, Math.Max(12, item.Height - 17)), new Point(24, item.Height), new Point(50, Math.Max(12, item.Height - 17))]),
            Fill = bubble.Background,
            Stroke = brush,
            StrokeThickness = item.Thickness,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        grid.Children.Add(tail);
        grid.Children.Add(bubble);
        return grid;
    }

    private static DoubleCollection? DashArray(AnnotationItem item) => item.Dashed ? new DoubleCollection([4, 3]) : null;

    private static Brush FillBrush(AnnotationItem item)
    {
        if (item.FillOpacity <= 0) return Brushes.Transparent;
        if (BrushFrom(item.Color) is not SolidColorBrush color) return Brushes.Transparent;
        return new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(item.FillOpacity, 0, 1)), color.Color.R, color.Color.G, color.Color.B));
    }

    private FrameworkElement EffectElement(AnnotationItem item, bool pixelate)
    {
        var rect = Bounds(item);
        var cropRect = new Int32Rect(
            Math.Clamp((int)rect.X, 0, Math.Max(0, _source.PixelWidth - 1)),
            Math.Clamp((int)rect.Y, 0, Math.Max(0, _source.PixelHeight - 1)),
            Math.Clamp((int)Math.Max(1, rect.Width), 1, Math.Max(1, _source.PixelWidth - Math.Clamp((int)rect.X, 0, Math.Max(0, _source.PixelWidth - 1)))),
            Math.Clamp((int)Math.Max(1, rect.Height), 1, Math.Max(1, _source.PixelHeight - Math.Clamp((int)rect.Y, 0, Math.Max(0, _source.PixelHeight - 1)))));
        var crop = new CroppedBitmap(_source, cropRect);
        crop.Freeze();
        var image = new Image
        {
            Source = pixelate ? Pixelate(crop) : crop,
            Stretch = Stretch.Fill,
            Effect = pixelate ? null : new BlurEffect { Radius = Math.Max(8, item.Thickness * 2) },
            Tag = item.Id
        };
        if (pixelate) RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var border = new Border { Child = image, ClipToBounds = true, Tag = item.Id };
        ApplyEffectClip(border, item);
        Place(border, item);
        return border;
    }

    private FrameworkElement MagnifyElement(AnnotationItem item)
    {
        var rect = Bounds(item);
        const double zoom = 2.5;
        var cropWidth = Math.Max(1, rect.Width / zoom);
        var cropHeight = Math.Max(1, rect.Height / zoom);
        var x = rect.X + (rect.Width - cropWidth) / 2;
        var y = rect.Y + (rect.Height - cropHeight) / 2;
        var ix = Math.Clamp((int)x, 0, Math.Max(0, _source.PixelWidth - 1));
        var iy = Math.Clamp((int)y, 0, Math.Max(0, _source.PixelHeight - 1));
        var cropRect = new Int32Rect(
            ix, iy,
            Math.Clamp((int)Math.Max(1, cropWidth), 1, Math.Max(1, _source.PixelWidth - ix)),
            Math.Clamp((int)Math.Max(1, cropHeight), 1, Math.Max(1, _source.PixelHeight - iy)));
        var crop = new CroppedBitmap(_source, cropRect);
        crop.Freeze();
        var image = new Image { Source = crop, Stretch = Stretch.Fill, Tag = item.Id };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var border = new Border
        {
            Child = image,
            ClipToBounds = true,
            BorderBrush = BrushFrom(item.Color),
            BorderThickness = new Thickness(Math.Max(1, item.Thickness / 2)),
            Tag = item.Id
        };
        ApplyEffectClip(border, item);
        Place(border, item);
        return border;
    }

    private static void ApplyEffectClip(FrameworkElement element, AnnotationItem item)
    {
        if (item.EffectShape == "Ellipse")
            element.Clip = new EllipseGeometry(new Rect(0, 0, Math.Max(1, item.Width), Math.Max(1, item.Height)));
    }

    private static BitmapSource Pixelate(BitmapSource source)
    {
        var width = Math.Max(1, source.PixelWidth / 14);
        var height = Math.Max(1, source.PixelHeight / 14);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawImage(source, new Rect(0, 0, width, height));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void Place(FrameworkElement element, AnnotationItem item)
    {
        element.Width = Math.Max(1, item.Width);
        element.Height = Math.Max(1, item.Height);
        Canvas.SetLeft(element, item.X);
        Canvas.SetTop(element, item.Y);
    }

    private Guid? FindItemId(DependencyObject? source)
    {
        var current = source;
        while (current is not null && current != AnnotationCanvas)
        {
            if (current is FrameworkElement { Tag: Guid id }) return id;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private AnnotationHandleTag? FindHandle(DependencyObject? source)
    {
        var current = source;
        while (current is not null && current != AnnotationCanvas)
        {
            if (current is FrameworkElement { Tag: AnnotationHandleTag handle }) return handle;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static Rect Bounds(AnnotationItem item)
    {
        if (item.Points.Count > 0)
        {
            var minX = item.Points.Min(point => point.X);
            var minY = item.Points.Min(point => point.Y);
            var maxX = item.Points.Max(point => point.X);
            var maxY = item.Points.Max(point => point.Y);
            return new Rect(minX, minY, Math.Max(2, maxX - minX), Math.Max(2, maxY - minY));
        }
        return new Rect(item.X, item.Y, Math.Max(2, item.Width), Math.Max(2, item.Height));
    }

    private void MoveItem(AnnotationItem item, double dx, double dy)
    {
        if (item.Points.Count > 0)
            item.Points = item.Points.Select(point => Clamp(new Point(point.X + dx, point.Y + dy))).ToList();
        else
        {
            item.X = Math.Clamp(item.X + dx, 0, Math.Max(0, Surface.Width - item.Width));
            item.Y = Math.Clamp(item.Y + dy, 0, Math.Max(0, Surface.Height - item.Height));
        }
    }

    private void TransformItem(AnnotationItem item, Point point)
    {
        var original = _transformOriginal;
        if (original is null) return;
        var originalBounds = Bounds(original);
        if (_transformMode == "Rotate")
        {
            var center = new Point(originalBounds.X + originalBounds.Width / 2, originalBounds.Y + originalBounds.Height / 2);
            var startAngle = Math.Atan2(_transformStart.Y - center.Y, _transformStart.X - center.X);
            var currentAngle = Math.Atan2(point.Y - center.Y, point.X - center.X);
            item.Rotation = original.Rotation + (currentAngle - startAngle) * 180 / Math.PI;
            return;
        }

        var dx = point.X - _transformStart.X;
        var dy = point.Y - _transformStart.Y;
        var left = originalBounds.Left;
        var right = originalBounds.Right;
        var top = originalBounds.Top;
        var bottom = originalBounds.Bottom;
        if (_transformMode.Contains('W')) left = Math.Min(right - 12, Math.Max(0, left + dx));
        if (_transformMode.Contains('E')) right = Math.Max(left + 12, Math.Min(Surface.Width, right + dx));
        if (_transformMode.Contains('N')) top = Math.Min(bottom - 12, Math.Max(0, top + dy));
        if (_transformMode.Contains('S')) bottom = Math.Max(top + 12, Math.Min(Surface.Height, bottom + dy));
        var resized = new Rect(left, top, right - left, bottom - top);

        if (original.Points.Count > 0)
        {
            var scaleX = resized.Width / Math.Max(1, originalBounds.Width);
            var scaleY = resized.Height / Math.Max(1, originalBounds.Height);
            item.Points = original.Points.Select(source => new Point(
                resized.X + (source.X - originalBounds.X) * scaleX,
                resized.Y + (source.Y - originalBounds.Y) * scaleY)).ToList();
        }
        else
        {
            item.X = resized.X; item.Y = resized.Y; item.Width = resized.Width; item.Height = resized.Height;
        }
    }

    private Point Clamp(Point point) => new(
        Math.Clamp(point.X, 0, Surface.Width),
        Math.Clamp(point.Y, 0, Surface.Height));

    private static double Distance(Point a, Point b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    private string SelectedColor() => _selectedColor;

    private void LoadStyleSettings()
    {
        _fillEnabled = _settings.AnnotationFillEnabled;
        _dashed = _settings.AnnotationDashed;
        FillOpacityBox.SelectedValue = _settings.AnnotationFillOpacity.ToString();
        if (FillOpacityBox.SelectedIndex < 0) FillOpacityBox.SelectedValue = "20";
        ArrowHeadsBox.SelectedValue = _settings.AnnotationArrowHeads;
        if (ArrowHeadsBox.SelectedIndex < 0) ArrowHeadsBox.SelectedValue = "End";
        EffectShapeBox.SelectedValue = "Rectangle";
        ObjectOpacitySlider.Value = _settings.AnnotationOpacity;
        StylePresetBox.SelectedValue = _settings.AnnotationStylePreset;
        if (StylePresetBox.SelectedIndex < 0) StylePresetBox.SelectedValue = "Outline";
        _textBold = _settings.AnnotationTextBold;
        _textItalic = _settings.AnnotationTextItalic;
        _textUnderline = _settings.AnnotationTextUnderline;
        _textStrikethrough = _settings.AnnotationTextStrikethrough;
        _textAlignment = Enum.TryParse<TextAlignment>(_settings.AnnotationTextAlignment, out var alignment) ? alignment : TextAlignment.Left;
        _textOutline = _settings.AnnotationTextOutline;
        TextFontBox.SelectedValue = _settings.AnnotationFontFamily;
        if (TextFontBox.SelectedIndex < 0) TextFontBox.SelectedValue = "Segoe UI";
        TextSizeBox.SelectedValue = Math.Clamp((int)Math.Round(ThicknessSlider.Value * 4), 9, 72).ToString();
        if (TextSizeBox.SelectedIndex < 0) TextSizeBox.SelectedValue = "16";
        SetSelectedColor(_selectedColor);
        UpdateStyleButtons();
        UpdateToolSizeLabels();
        _loadingStyle = false;
    }

    private void ApplyCurrentStyle(AnnotationItem item)
    {
        item.Opacity = ObjectOpacitySlider.Value / 100.0;
        item.FillOpacity = _fillEnabled ? SelectedFillOpacity() : 0;
        item.Dashed = _dashed;
        item.ArrowHeads = ArrowHeadsBox.SelectedValue as string ?? "End";
        item.EffectShape = EffectShapeBox.SelectedValue as string ?? "Rectangle";
        if (item.Kind is AnnotationKind.Text or AnnotationKind.Callout)
        {
            item.FontFamily = TextFontBox.SelectedValue as string ?? "Segoe UI";
            item.Bold = _textBold;
            item.Italic = _textItalic;
            item.Underline = _textUnderline;
            item.Strikethrough = _textStrikethrough;
            item.TextAlignment = _textAlignment.ToString();
            item.Outline = _textOutline;
        }
    }

    private double SelectedFillOpacity() =>
        double.TryParse(FillOpacityBox.SelectedValue as string, out var value) ? Math.Clamp(value / 100.0, 0, 1) : 0.2;

    private void SyncStyleControls(AnnotationItem item)
    {
        _loadingStyle = true;
        SetSelectedColor(item.Color);
        ThicknessSlider.Value = item.Thickness;
        _fillEnabled = item.FillOpacity > 0;
        _dashed = item.Dashed;
        var fillPercent = new[] { 0, 20, 40, 65 }.OrderBy(value => Math.Abs(value - item.FillOpacity * 100)).First();
        FillOpacityBox.SelectedValue = fillPercent.ToString();
        ArrowHeadsBox.SelectedValue = item.ArrowHeads;
        EffectShapeBox.SelectedValue = item.EffectShape;
        ObjectOpacitySlider.Value = item.Opacity * 100;
        _textBold = item.Bold;
        _textItalic = item.Italic;
        _textUnderline = item.Underline;
        _textStrikethrough = item.Strikethrough;
        _textAlignment = Enum.TryParse<TextAlignment>(item.TextAlignment, out var alignment) ? alignment : TextAlignment.Left;
        _textOutline = item.Outline;
        TextFontBox.SelectedValue = item.FontFamily;
        TextSizeBox.SelectedValue = Math.Clamp((int)Math.Round(item.Thickness * 4), 9, 72).ToString();
        UpdateStyleButtons();
        UpdateToolSizeLabels();
        _loadingStyle = false;
    }

    private void StyleControl_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingStyle) return;
        if (sender == ColorBox && ColorBox.SelectedValue is string color)
        {
            _selectedColor = color;
            SelectedColorFill.Background = BrushFrom(color);
        }
        _toolSizes[ToolSizeKey(_tool)] = ThicknessSlider.Value;
        UpdateToolSizeLabels();
        if (sender == ThicknessSlider && _tool is "Eraser" or "Blur") UpdateSurfaceCursor(_tool);
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void FillToggle_Click(object sender, RoutedEventArgs e)
    {
        _fillEnabled = !_fillEnabled;
        UpdateStyleButtons();
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void DashToggle_Click(object sender, RoutedEventArgs e)
    {
        _dashed = !_dashed;
        UpdateStyleButtons();
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void StylePreset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingStyle) return;
        _loadingStyle = true;
        var preset = StylePresetBox.SelectedValue as string ?? "Outline";
        switch (preset)
        {
            case "Filled": _fillEnabled = true; _dashed = false; FillOpacityBox.SelectedValue = "40"; ObjectOpacitySlider.Value = 100; break;
            case "Dashed": _fillEnabled = false; _dashed = true; ObjectOpacitySlider.Value = 100; break;
            case "Emphasis": _fillEnabled = true; _dashed = false; FillOpacityBox.SelectedValue = "20"; ObjectOpacitySlider.Value = 100; ThicknessSlider.Value = Math.Max(6, ThicknessSlider.Value); break;
            default: _fillEnabled = false; _dashed = false; ObjectOpacitySlider.Value = 100; break;
        }
        UpdateStyleButtons();
        _loadingStyle = false;
        ApplyStyleToSelected();
        SaveStyleSettings();
    }

    private void ApplyStyleToSelected()
    {
        if (_selectedId is not Guid id || _items.FirstOrDefault(item => item.Id == id) is not { } selected) return;
        PushUndo();
        selected.Color = SelectedColor();
        selected.Thickness = ThicknessSlider.Value;
        ApplyCurrentStyle(selected);
        RenderAnnotations();
    }

    private void UpdateStyleButtons()
    {
        FillToggleButton.Background = _fillEnabled ? new SolidColorBrush(Color.FromRgb(214, 231, 255)) : Brushes.Transparent;
        DashToggleButton.Background = _dashed ? new SolidColorBrush(Color.FromRgb(214, 231, 255)) : Brushes.Transparent;
        FillOpacityBox.Visibility = FillToggleButton.Visibility == Visibility.Visible && _fillEnabled ? Visibility.Visible : Visibility.Collapsed;
        TextBoldButton.Background = _textBold ? new SolidColorBrush(Color.FromRgb(214, 231, 255)) : Brushes.Transparent;
        TextItalicButton.Background = _textItalic ? new SolidColorBrush(Color.FromRgb(214, 231, 255)) : Brushes.Transparent;
        TextUnderlineButton.Background = _textUnderline ? new SolidColorBrush(Color.FromRgb(214, 231, 255)) : Brushes.Transparent;
        TextStrikeButton.Background = _textStrikethrough ? new SolidColorBrush(Color.FromRgb(214, 231, 255)) : Brushes.Transparent;
        TextAlignButton.Content = _textAlignment switch { TextAlignment.Center => "≡", TextAlignment.Right => "☷", _ => "☰" };
        TextAlignButton.Background = _textAlignment == TextAlignment.Left ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(214, 231, 255));
        TextOutlineButton.Background = _textOutline ? new SolidColorBrush(Color.FromRgb(214, 231, 255)) : Brushes.Transparent;
        SelectedColorFill.Background = BrushFrom(SelectedColor());
    }

    private void SaveStyleSettings()
    {
        _settings.AnnotationFillEnabled = _fillEnabled;
        _settings.AnnotationFillOpacity = (int)Math.Round(SelectedFillOpacity() * 100);
        _settings.AnnotationOpacity = (int)Math.Round(ObjectOpacitySlider.Value);
        _settings.AnnotationDashed = _dashed;
        _settings.AnnotationArrowHeads = ArrowHeadsBox.SelectedValue as string ?? "End";
        _settings.AnnotationStylePreset = StylePresetBox.SelectedValue as string ?? "Outline";
        _settings.AnnotationFontFamily = TextFontBox.SelectedValue as string ?? "Segoe UI";
        _settings.AnnotationTextBold = _textBold;
        _settings.AnnotationTextItalic = _textItalic;
        _settings.AnnotationTextUnderline = _textUnderline;
        _settings.AnnotationTextStrikethrough = _textStrikethrough;
        _settings.AnnotationTextAlignment = _textAlignment.ToString();
        _settings.AnnotationTextOutline = _textOutline;
        RememberCurrentToolSize();
        _settings.AnnotationShapeSize = _toolSizes["Shape"];
        _settings.AnnotationArrowSize = _toolSizes["Arrow"];
        _settings.AnnotationPencilSize = _toolSizes["Pencil"];
        _settings.AnnotationMarkerSize = _toolSizes["Marker"];
        _settings.AnnotationBlurSize = _toolSizes["Blur"];
        _settings.AnnotationTextSize = _toolSizes["Text"];
        _settings.AnnotationEraserSize = _toolSizes["Eraser"];
        SettingsService.Save(_settings);
    }

    private static Brush BrushFrom(string color)
    {
        try { return (Brush)new BrushConverter().ConvertFromString(color)!; }
        catch { return Brushes.Red; }
    }

    private static TextDecorationCollection TextDecorationsFor(bool underline, bool strikethrough)
    {
        var decorations = new TextDecorationCollection();
        if (underline)
            foreach (var decoration in TextDecorations.Underline) decorations.Add(decoration);
        if (strikethrough)
            foreach (var decoration in TextDecorations.Strikethrough) decorations.Add(decoration);
        return decorations;
    }

    private static List<AnnotationItem> Clone(IEnumerable<AnnotationItem> items) => items.Select(item => item.Clone()).ToList();

    private void PushUndo()
    {
        _undo.Push(Clone(_items));
        _redo.Clear();
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    private void Undo()
    {
        CommitTextEditor();
        if (_undo.Count == 0) return;
        _redo.Push(Clone(_items));
        _items = _undo.Pop();
        _selectedId = null;
        RenderAnnotations();
    }

    private void Redo()
    {
        CommitTextEditor();
        if (_redo.Count == 0) return;
        _undo.Push(Clone(_items));
        _items = _redo.Pop();
        _selectedId = null;
        RenderAnnotations();
    }

    public BitmapSource Flatten()
    {
        CommitTextEditor();
        var selected = _selectedId;
        var cursorVisibility = ToolCursorLayer.Visibility;
        _selectedId = null;
        ToolCursorLayer.Visibility = Visibility.Collapsed;
        RenderAnnotations();
        Surface.Measure(new Size(_source.PixelWidth, _source.PixelHeight));
        Surface.Arrange(new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
        Surface.UpdateLayout();
        var result = new RenderTargetBitmap(_source.PixelWidth, _source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(Surface);
        result.Freeze();
        _selectedId = selected;
        ToolCursorLayer.Visibility = cursorVisibility;
        RenderAnnotations();
        return result;
    }

    internal AnnotationAppliedEventArgs SnapshotDocument()
    {
        var image = Flatten();
        return new AnnotationAppliedEventArgs(_source, image, Clone(_items));
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var image = Flatten();
        Clipboard.SetImage(image);
        DocumentStored?.Invoke(new AnnotationAppliedEventArgs(_source, image, Clone(_items)));
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = CaptureService.ImageSaveFilter,
            DefaultExt = CaptureService.ExtensionForFormat(_settings.OutputFormat),
            FilterIndex = CaptureService.FilterIndexForFormat(_settings.OutputFormat),
            AddExtension = true,
            FileName = Path.GetFileName(SettingsService.CreateOutputPath(Path.GetTempPath(), _settings.OutputFileName))
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            var image = Flatten();
            CaptureService.SaveImage(image, dialog.FileName, _settings.ImageQuality, _settings);
            DocumentStored?.Invoke(new AnnotationAppliedEventArgs(_source, image, Clone(_items)));
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        var image = Flatten();
        Clipboard.SetImage(image);
        Applied?.Invoke(new AnnotationAppliedEventArgs(_source, image, Clone(_items)));
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Undo(); e.Handled = true; }
        else if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { Redo(); e.Handled = true; }
        else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { DuplicateSelected(); e.Handled = true; }
        else if (e.Key == Key.OemCloseBrackets && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { ReorderSelected(1); e.Handled = true; }
        else if (e.Key == Key.OemOpenBrackets && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { ReorderSelected(-1); e.Handled = true; }
        else if (_selectedId is not null && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            MoveSelected(e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0,
                e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete && _selectedId is Guid id)
        {
            DeleteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) Cancelled?.Invoke(this, EventArgs.Empty);
    }
}
