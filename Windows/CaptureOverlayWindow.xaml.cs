using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using SnapPin.Services;
using SnapPin.Models;
using SnapPin.Controls;
using Forms = System.Windows.Forms;

namespace SnapPin.Windows;

public enum CaptureCompletionMode
{
    Interactive,
    CopyOnSelection,
    RecordOnSelection,
    FullScreenDraw
}

public partial class CaptureOverlayWindow : Window
{
    private enum RecordingReviewChoice { KeepFull, SaveTrimmed, Discard }

    internal bool IsDrawingInline => _completionMode == CaptureCompletionMode.FullScreenDraw && _annotationMode;
    internal Rect CurrentSelection => _selection;

    private readonly BitmapSource _screen;
    private readonly AppSettings _settings;
    private readonly CaptureCompletionMode _completionMode;
    private readonly CaptureOptions _options;
    private System.Windows.Point _start;
    private Rect _selection;
    private Rect _detectedSelection;
    private bool _dragging;
    private bool _resizing;
    private string _resizeHandle = string.Empty;
    private Point _resizeStart;
    private Rect _resizeStartRect;
    private IReadOnlyList<DetectedScreenRegion> _detectionCandidates = [];
    private int _detectionIndex;
    private bool _detectElements = true;
    private CancellationTokenSource? _ocrCancellation;
    private string? _ocrHistoryRecordId;
    private bool _ocrLayoutMode;
    private bool _ocrAreaSelectionMode;
    private bool _ocrAreaDragging;
    private Point _ocrAreaStart;
    private Rect _ocrAreaSelection;
    private bool _initialized;
    private bool _longCaptureRunning;
    private bool _recordingRunning;
    private bool _recordingPaused;
    private bool _recordingStopRequested;
    private CancellationTokenSource? _recordingCancellation;
    private AdvancedRecordingSession? _advancedRecording;
    private bool _recordingReviewRunning;
    private bool _recordingPreviewPlaying;
    private bool _recordingReviewDragging;
    private bool _recordingReviewPositioned;
    private Point _recordingReviewDragStart;
    private Point _recordingReviewStart;
    private TaskCompletionSource<RecordingReviewChoice>? _recordingReviewChoice;
    private IntPtr _recordingTargetWindow;
    private IntPtr _overlayHandle;
    private readonly Stopwatch _detectionClock = Stopwatch.StartNew();
    private bool _annotationMode;
    private string? _annotationHistoryRecordId;
    private IReadOnlyList<AnnotationItem>? _annotationResizeItems;
    private Int32Rect _annotationResizePixelRegion;
    private readonly IReadOnlyList<CaptureRegion> _regionHistory;
    private int _regionHistoryIndex = -1;

    public CaptureOverlayWindow(BitmapSource screen, CaptureCompletionMode completionMode = CaptureCompletionMode.Interactive, CaptureOptions? options = null)
    {
        InitializeComponent();
        _screen = screen;
        _regionHistory = HistoryService.RecentRegions();
        _settings = SettingsService.Load();
        ToolbarThemeService.ApplyTo(Resources, _settings.ToolbarSizeMode);
        ApplyCaptureAnnotationToolbarConfiguration(_settings.AnnotationToolbarOrder, _settings.AnnotationToolbarEnabled);
        ApplyCaptureToolbarConfiguration(_settings.CaptureToolbarOrder, _settings.CaptureToolbarEnabled);
        _completionMode = completionMode;
        _options = options ?? new CaptureOptions();
        ScreenImage.Source = screen;
        CaptureInlineEditor.Applied += ApplyCaptureAnnotation;
        CaptureInlineEditor.DocumentStored += StoreCaptureAnnotation;
        CaptureInlineEditor.Cancelled += (_, _) => Close();
        SelectionRect.StrokeThickness = _settings.CaptureBorderWidth;
        var selectionColor = _settings.CaptureBorderColor.Equals("#63E6BE", StringComparison.OrdinalIgnoreCase) ? "#3388FF" : _settings.CaptureBorderColor;
        TrySetBrush(SelectionRect, System.Windows.Shapes.Shape.StrokeProperty, selectionColor);
        TrySetBrush(MaskLayer, System.Windows.Shapes.Shape.FillProperty, _settings.CaptureMaskColor);
        foreach (var mask in new[] { MaskTop, MaskLeft, MaskRight, MaskBottom })
            TrySetBrush(mask, System.Windows.Shapes.Shape.FillProperty, _settings.CaptureMaskColor);
        CrossHorizontal.Visibility = _settings.ShowCrossLines ? Visibility.Visible : Visibility.Collapsed;
        CrossVertical.Visibility = _settings.ShowCrossLines ? Visibility.Visible : Visibility.Collapsed;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        SourceInitialized += (_, _) => FitToVirtualScreenPixels();
        CaptureOcrLanguageBox.SelectedValue = _settings.OcrLanguage;
        if (CaptureOcrLanguageBox.SelectedIndex < 0) CaptureOcrLanguageBox.SelectedIndex = 3;
        OcrAutoCopyBox.IsChecked = _settings.OcrAutoCopy;
        ShortcutHints.Visibility = CaptureHintsVisibility;
        DetectionWheelHint.Visibility = _settings.ShowElementDetection == false ? Visibility.Collapsed : Visibility.Visible;
        DetectionTabHint.Visibility = _settings.ShowElementDetection == false ? Visibility.Collapsed : Visibility.Visible;
        DetectionModeText.Visibility = _settings.ShowElementDetection == false ? Visibility.Collapsed : Visibility.Visible;
        Loaded += (_, _) =>
        {
            _overlayHandle = new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowDisplayAffinity(_overlayHandle, NativeMethods.WdaExcludeFromCapture);
            _initialized = true;
            PositionFloatingPanels();
            Focus();
            if (_completionMode == CaptureCompletionMode.FullScreenDraw)
                Dispatcher.BeginInvoke(InitializeFullScreenDrawing);
        };
        Closed += (_, _) =>
        {
            _ocrCancellation?.Cancel();
            _recordingCancellation?.Cancel();
        };
    }

    internal void ApplyCaptureToolbarConfiguration(IEnumerable<string>? order, IEnumerable<string>? enabled)
    {
        var normalizedOrder = CaptureToolbarCatalog.NormalizeOrder(order);
        var normalizedEnabled = CaptureToolbarCatalog.NormalizeEnabled(enabled).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var buttons = CaptureActionPanel.Children.OfType<Button>()
            .Where(button => button.Tag is string)
            .ToDictionary(button => (string)button.Tag, StringComparer.OrdinalIgnoreCase);
        foreach (var button in buttons.Values.ToList()) CaptureActionPanel.Children.Remove(button);
        foreach (var key in normalizedOrder)
        {
            if (!buttons.TryGetValue(key, out var button)) continue;
            button.Visibility = normalizedEnabled.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
            CaptureActionPanel.Children.Add(button);
        }
    }

    internal void ApplyCaptureAnnotationToolbarConfiguration(IEnumerable<string>? order, IEnumerable<string>? enabled)
    {
        var normalizedOrder = AnnotationToolbarCatalog.NormalizeOrder(order);
        var normalizedEnabled = AnnotationToolbarCatalog.NormalizeEnabled(enabled).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var buttons = CaptureAnnotationToolPanel.Children.OfType<Button>()
            .Where(button => button.Tag is string)
            .ToDictionary(button => (string)button.Tag, StringComparer.OrdinalIgnoreCase);
        foreach (var button in buttons.Values.ToList()) CaptureAnnotationToolPanel.Children.Remove(button);
        foreach (var key in normalizedOrder)
        {
            if (!buttons.TryGetValue(key, out var button)) continue;
            button.Visibility = normalizedEnabled.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
            CaptureAnnotationToolPanel.Children.Add(button);
        }
    }

    private Visibility CaptureHintsVisibility => _settings.ShowCaptureHints ? Visibility.Visible : Visibility.Collapsed;

    private void InitializeFullScreenDrawing()
    {
        if (!IsVisible || ActualWidth < 1 || ActualHeight < 1) return;
        _selection = new Rect(0, 0, ActualWidth, ActualHeight);
        RenderSelection();
        BeginInlineAnnotation();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_annotationMode) return;
        if (_ocrAreaSelectionMode)
        {
            BeginOcrAreaDrag(e.GetPosition(OverlayCanvas));
            e.Handled = true;
            return;
        }
        if (_recordingRunning || ActionBar.IsMouseOver || OcrPanel.IsMouseOver || RecordingBar.IsMouseOver || RecordingReviewPanel.IsMouseOver || e.OriginalSource is System.Windows.Controls.Button) return;
        _ocrCancellation?.Cancel();
        OcrPanel.Visibility = Visibility.Collapsed;
        _dragging = true;
        _start = e.GetPosition(OverlayCanvas);
        _selection = new Rect(_start, _start);
        SelectionRect.Visibility = Visibility.Visible;
        SizeBadge.Visibility = _settings.ShowCaptureSize ? Visibility.Visible : Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        SelectionHandles.Visibility = Visibility.Collapsed;
        DetectionRect.Visibility = Visibility.Collapsed;
        CaptureMouse();
        UpdateSelection(e.GetPosition(OverlayCanvas));
    }

    private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(OverlayCanvas);
        if (_annotationMode)
        {
            if (_resizing) UpdateResize(point);
            return;
        }
        if (_ocrAreaSelectionMode)
        {
            if (_ocrAreaDragging) UpdateOcrAreaDrag(point);
            return;
        }
        if (_settings.ShowCrossLines)
        {
            CrossHorizontal.X1 = 0;
            CrossHorizontal.X2 = ActualWidth;
            CrossHorizontal.Y1 = CrossHorizontal.Y2 = point.Y;
            CrossVertical.Y1 = 0;
            CrossVertical.Y2 = ActualHeight;
            CrossVertical.X1 = CrossVertical.X2 = point.X;
        }
        if (_resizing)
            UpdateResize(point);
        else if (_dragging)
            UpdateSelection(point);
        else if (_settings.ShowElementDetection != false && _selection.Width < 3 && _selection.Height < 3 && _detectionClock.ElapsedMilliseconds >= 110)
        {
            _detectionClock.Restart();
            UpdateDetectedRegion(point);
        }
        else if (_selection.Width >= 3 || _settings.ShowElementDetection == false)
        {
            DetectionRect.Visibility = Visibility.Collapsed;
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_annotationMode)
        {
            if (_resizing)
            {
                _resizing = false;
                ReleaseMouseCapture();
                RefreshInlineAnnotationAfterResize();
                SetResizeToolbarsVisible(true);
                PositionActionBar();
                e.Handled = true;
            }
            return;
        }
        if (_ocrAreaSelectionMode)
        {
            FinishOcrAreaDrag(e.GetPosition(OverlayCanvas));
            e.Handled = true;
            return;
        }
        if (_resizing)
        {
            _resizing = false;
            ReleaseMouseCapture();
            RenderSelection();
            SetResizeToolbarsVisible(true);
            PositionActionBar();
            e.Handled = true;
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        UpdateSelection(e.GetPosition(OverlayCanvas));

        if (_selection.Width < 3 || _selection.Height < 3)
        {
            if (_detectedSelection.Width >= 3 && _detectedSelection.Height >= 3)
            {
                _selection = _detectedSelection;
                RenderSelection();
            }
            else
            {
                ResetSelection();
                return;
            }
        }

        if (_completionMode == CaptureCompletionMode.CopyOnSelection)
        {
            CompleteCopy();
            return;
        }
        if (_completionMode == CaptureCompletionMode.RecordOnSelection)
        {
            _ = RunRecordingAsync();
            return;
        }
        DetectionRect.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Visible;
        PositionActionBar();
    }

    private void UpdateSelection(System.Windows.Point current)
    {
        var width = Math.Abs(current.X - _start.X);
        var height = Math.Abs(current.Y - _start.Y);
        var fixedWidth = _options.FixedWidth > 0 ? _options.FixedWidth * ActualWidth / _screen.PixelWidth : 0;
        var fixedHeight = _options.FixedHeight > 0 ? _options.FixedHeight * ActualHeight / _screen.PixelHeight : 0;
        if (fixedWidth > 0) width = fixedWidth;
        if (fixedHeight > 0) height = fixedHeight;

        if (_options.AspectRatio > 0)
        {
            var displayRatio = _options.AspectRatio * ActualWidth / _screen.PixelWidth / (ActualHeight / _screen.PixelHeight);
            if (fixedWidth > 0 && fixedHeight <= 0)
                height = width / displayRatio;
            else if (fixedHeight > 0 && fixedWidth <= 0)
                width = height * displayRatio;
            else if (fixedWidth <= 0 || fixedHeight <= 0)
            {
                if (height <= 0 || width / height >= displayRatio) height = width / displayRatio;
                else width = height * displayRatio;
            }
        }

        var x = current.X >= _start.X ? _start.X : _start.X - width;
        var y = current.Y >= _start.Y ? _start.Y : _start.Y - height;
        x = Math.Clamp(x, 0, ActualWidth);
        y = Math.Clamp(y, 0, ActualHeight);
        width = Math.Min(width, ActualWidth - x);
        height = Math.Min(height, ActualHeight - y);
        _selection = new Rect(x, y, Math.Max(0, width), Math.Max(0, height));

        RenderSelection();
    }

    private void RenderSelection()
    {
        if (_selection.Width >= 3 && _selection.Height >= 3)
            DetectionRect.Visibility = Visibility.Collapsed;
        Canvas.SetLeft(SelectionRect, _selection.X);
        Canvas.SetTop(SelectionRect, _selection.Y);
        SelectionRect.Width = _selection.Width;
        SelectionRect.Height = _selection.Height;
        SelectionRect.Visibility = Visibility.Visible;
        SelectionHandles.Visibility = Visibility.Visible;
        PositionSelectionHandles();
        RenderSelectionMask();

        var pixelRegion = SelectedPixelRegion();
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        SizeText.Text = $"{virtualBounds.Left + pixelRegion.X}, {virtualBounds.Top + pixelRegion.Y}   {pixelRegion.Width} × {pixelRegion.Height} px";
        SizeBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(SizeBadge, Math.Min(ActualWidth - 100, _selection.X));
        Canvas.SetTop(SizeBadge, Math.Max(4, _selection.Y - 36));
    }

    private void UpdateDetectedRegion(Point overlayPoint)
    {
        if (_overlayHandle == IntPtr.Zero) return;
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        var screenPoint = OverlayToScreen(overlayPoint);
        _detectionCandidates = ElementDetectionService.DetectHierarchy(screenPoint, _overlayHandle);
        if (_detectionCandidates.Count == 0)
        {
            _detectedSelection = Rect.Empty;
            DetectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        _detectionIndex = _detectElements ? _detectionCandidates.Count - 1 : 0;
        RenderDetectedCandidate();
    }

    private void RenderDetectedCandidate()
    {
        if (_detectionCandidates.Count == 0) return;
        _detectionIndex = Math.Clamp(_detectionIndex, 0, _detectionCandidates.Count - 1);
        var detected = _detectionCandidates[_detectionIndex];
        var virtualBounds = Forms.SystemInformation.VirtualScreen;

        var topLeft = ScreenToOverlay(new Point(detected.Bounds.Left, detected.Bounds.Top));
        var bottomRight = ScreenToOverlay(new Point(detected.Bounds.Right, detected.Bounds.Bottom));
        _detectedSelection = Rect.Intersect(
            new Rect(0, 0, ActualWidth, ActualHeight),
            new Rect(topLeft, bottomRight));
        if (_detectedSelection.IsEmpty)
        {
            DetectionRect.Visibility = Visibility.Collapsed;
            return;
        }
        Canvas.SetLeft(DetectionRect, _detectedSelection.X);
        Canvas.SetTop(DetectionRect, _detectedSelection.Y);
        DetectionRect.Width = _detectedSelection.Width;
        DetectionRect.Height = _detectedSelection.Height;
        DetectionRect.Visibility = Visibility.Visible;
        DetectionModeText.Text = $"{L("Mode")}: {L(_detectElements ? "UI element" : "window")}   •   {_detectionIndex + 1}/{_detectionCandidates.Count}   •   {detected.Name}";
    }

    private void PositionActionBar()
    {
        var originalVisibility = ActionBar.Visibility;
        if (originalVisibility == Visibility.Collapsed) ActionBar.Visibility = Visibility.Hidden;
        ActionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = ActionBar.DesiredSize.Width;
        var height = ActionBar.DesiredSize.Height;
        var placement = OverlayLayoutService.PlaceBelowAndKeepVisible(_selection, new Size(width, height), new Size(ActualWidth, ActualHeight));
        Canvas.SetLeft(ActionBar, placement.X);
        var reservedHeight = _annotationMode
            ? 90
            : OcrPanel.Visibility == Visibility.Visible ? OcrPanel.Height + 5 : 0;
        var maximumTop = Math.Max(8, ActualHeight - height - reservedHeight - 8);
        Canvas.SetTop(ActionBar, Math.Min(placement.Y, maximumTop));
        ActionBar.Visibility = originalVisibility;
        if (_annotationMode && CaptureInlineEditor.Visibility == Visibility.Visible)
        {
            var propertyTop = Canvas.GetTop(ActionBar) + Math.Max(1, height) + 2;
            CaptureInlineEditor.UpdateCaptureToolbarPosition(Canvas.GetLeft(ActionBar), propertyTop);
        }
    }

    private Rect ActionBarBounds()
    {
        ActionBar.UpdateLayout();
        var width = ActionBar.ActualWidth > 1 ? ActionBar.ActualWidth : ActionBar.DesiredSize.Width;
        var height = ActionBar.ActualHeight > 1 ? ActionBar.ActualHeight : ActionBar.DesiredSize.Height;
        return new Rect(Canvas.GetLeft(ActionBar), Canvas.GetTop(ActionBar), Math.Max(1, width), Math.Max(1, height));
    }

    private void PositionSelectionHandles()
    {
        SelectionHandles.Width = Math.Max(1, ActualWidth);
        SelectionHandles.Height = Math.Max(1, ActualHeight);
        const double radius = 6.5;
        var centerX = _selection.X + _selection.Width / 2;
        var centerY = _selection.Y + _selection.Height / 2;
        SetHandle(HandleNW, _selection.Left - radius, _selection.Top - radius);
        SetHandle(HandleN, centerX - radius, _selection.Top - radius);
        SetHandle(HandleNE, _selection.Right - radius, _selection.Top - radius);
        SetHandle(HandleE, _selection.Right - radius, centerY - radius);
        SetHandle(HandleSE, _selection.Right - radius, _selection.Bottom - radius);
        SetHandle(HandleS, centerX - radius, _selection.Bottom - radius);
        SetHandle(HandleSW, _selection.Left - radius, _selection.Bottom - radius);
        SetHandle(HandleW, _selection.Left - radius, centerY - radius);
    }

    private void RenderSelectionMask()
    {
        MaskLayer.Visibility = Visibility.Collapsed;
        SelectionMask.Visibility = Visibility.Visible;
        SelectionMask.Width = Math.Max(1, ActualWidth);
        SelectionMask.Height = Math.Max(1, ActualHeight);
        SetMask(MaskTop, 0, 0, ActualWidth, _selection.Top);
        SetMask(MaskBottom, 0, _selection.Bottom, ActualWidth, Math.Max(0, ActualHeight - _selection.Bottom));
        SetMask(MaskLeft, 0, _selection.Top, _selection.Left, _selection.Height);
        SetMask(MaskRight, _selection.Right, _selection.Top, Math.Max(0, ActualWidth - _selection.Right), _selection.Height);
    }

    private static void SetMask(FrameworkElement mask, double left, double top, double width, double height)
    {
        Canvas.SetLeft(mask, left); Canvas.SetTop(mask, top);
        mask.Width = Math.Max(0, width); mask.Height = Math.Max(0, height);
    }

    private static void SetHandle(FrameworkElement handle, double left, double top)
    {
        Canvas.SetLeft(handle, left);
        Canvas.SetTop(handle, top);
    }

    private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string handle }) return;
        if (_annotationMode)
        {
            var document = CaptureInlineEditor.SnapshotDocument();
            _annotationResizeItems = document.Items.Select(item => item.Clone()).ToList();
            _annotationResizePixelRegion = SelectedPixelRegion();
        }
        _resizing = true;
        _resizeHandle = handle;
        _resizeStart = e.GetPosition(OverlayCanvas);
        _resizeStartRect = _selection;
        SetResizeToolbarsVisible(false);
        CaptureMouse();
        e.Handled = true;
    }

    private void SetResizeToolbarsVisible(bool visible)
    {
        ActionBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (_annotationMode)
            CaptureInlineEditor.SetCapturePropertiesToolbarVisible(visible);
    }

    private void RefreshInlineAnnotationAfterResize()
    {
        if (!_annotationMode || _annotationResizeItems is null) return;
        var newPixelRegion = SelectedPixelRegion();
        var shiftedItems = ShiftAnnotationsForRecrop(_annotationResizeItems, _annotationResizePixelRegion, newPixelRegion);
        _annotationResizeItems = null;
        CaptureInlineEditor.LoadImage(SelectedImage(), shiftedItems);
        CaptureInlineEditor.UpdateCaptureOverlayBounds(_selection, new Size(ActualWidth, ActualHeight), _selection);
        SelectionRect.Fill = Brushes.Transparent;
        SelectionRect.Visibility = Visibility.Visible;
        SelectionHandles.Visibility = Visibility.Visible;
        RenderSelection();
        CaptureInlineEditor.Focus();
    }

    internal static IReadOnlyList<AnnotationItem> ShiftAnnotationsForRecrop(
        IEnumerable<AnnotationItem> items, Int32Rect oldPixelRegion, Int32Rect newPixelRegion)
    {
        var offsetX = oldPixelRegion.X - newPixelRegion.X;
        var offsetY = oldPixelRegion.Y - newPixelRegion.Y;
        return items.Select(item =>
        {
            var shifted = item.Clone();
            shifted.X += offsetX;
            shifted.Y += offsetY;
            shifted.Points = shifted.Points.Select(point => new Point(point.X + offsetX, point.Y + offsetY)).ToList();
            return shifted;
        }).ToList();
    }

    private void UpdateResize(Point point)
    {
        var dx = point.X - _resizeStart.X;
        var dy = point.Y - _resizeStart.Y;
        var left = _resizeStartRect.Left;
        var right = _resizeStartRect.Right;
        var top = _resizeStartRect.Top;
        var bottom = _resizeStartRect.Bottom;
        if (_resizeHandle.Contains('W')) left = Math.Clamp(left + dx, 0, right - 3);
        if (_resizeHandle.Contains('E')) right = Math.Clamp(right + dx, left + 3, ActualWidth);
        if (_resizeHandle.Contains('N')) top = Math.Clamp(top + dy, 0, bottom - 3);
        if (_resizeHandle.Contains('S')) bottom = Math.Clamp(bottom + dy, top + 3, ActualHeight);
        _selection = new Rect(left, top, right - left, bottom - top);
        RenderSelection();
    }

    private void PositionFloatingPanels()
    {
        ShortcutHints.Width = Math.Min(330, Math.Max(220, ActualWidth - 32));
        OcrPanel.Width = Math.Min(580, Math.Max(300, ActualWidth - 36));
        OcrPanel.Height = Math.Min(390, Math.Max(250, ActualHeight - 36));
        Canvas.SetLeft(ShortcutHints, 16);
        ShortcutHints.Measure(new Size(ShortcutHints.Width, double.PositiveInfinity));
        Canvas.SetTop(ShortcutHints, Math.Max(16, ActualHeight - ShortcutHints.DesiredSize.Height - 18));
        if (ActionBar.Visibility == Visibility.Visible && _selection.Width >= 3 && _selection.Height >= 3)
        {
            PositionActionBar();
            var placement = OverlayLayoutService.PlaceBelowAndKeepVisible(
                ActionBarBounds(), new Size(OcrPanel.Width, OcrPanel.Height), new Size(ActualWidth, ActualHeight), 5);
            Canvas.SetLeft(OcrPanel, placement.X);
            Canvas.SetTop(OcrPanel, placement.Y);
        }
        else
        {
            Canvas.SetLeft(OcrPanel, Math.Max(10, ActualWidth - OcrPanel.Width - 18));
            Canvas.SetTop(OcrPanel, Math.Max(10, (ActualHeight - OcrPanel.Height) / 2));
        }
        PositionSelectionHandles();
        if (_selection.Width >= 3 && _selection.Height >= 3) RenderSelectionMask();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PositionFloatingPanels();
        if (_recordingRunning || _recordingReviewRunning) PositionRecordingControls();
        if (_annotationMode)
        {
            CaptureInlineEditor.UpdateCaptureOverlayBounds(_selection, new Size(ActualWidth, ActualHeight), _selection);
        }
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_annotationMode) return;
        if (_settings.ShowElementDetection == false || _selection.Width >= 3 || _dragging || _resizing || _detectionCandidates.Count <= 1) return;
        _detectionIndex = Math.Clamp(_detectionIndex + (e.Delta < 0 ? 1 : -1), 0, _detectionCandidates.Count - 1);
        RenderDetectedCandidate();
        e.Handled = true;
    }

    private BitmapSource SelectedImage()
        => CaptureService.Crop(_screen, SelectedPixelRegion());

    private Int32Rect SelectedPixelRegion() => PixelRegion(_selection);

    private Int32Rect PixelRegion(Rect area)
    {
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        var topLeft = OverlayToScreen(area.TopLeft);
        var bottomRight = OverlayToScreen(area.BottomRight);
        return CaptureCoordinateService.ToBitmapRegion(topLeft, bottomRight, virtualBounds, _screen.PixelWidth, _screen.PixelHeight);
    }

    private void FitToVirtualScreenPixels()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var bounds = Forms.SystemInformation.VirtualScreen;
        if (handle != IntPtr.Zero)
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private Point OverlayToScreen(Point point)
    {
        try { return OverlayCanvas.PointToScreen(point); }
        catch (InvalidOperationException)
        {
            var bounds = Forms.SystemInformation.VirtualScreen;
            return new Point(
                bounds.Left + point.X * _screen.PixelWidth / Math.Max(1, ActualWidth),
                bounds.Top + point.Y * _screen.PixelHeight / Math.Max(1, ActualHeight));
        }
    }

    private Point ScreenToOverlay(Point point)
    {
        try { return OverlayCanvas.PointFromScreen(point); }
        catch (InvalidOperationException)
        {
            var bounds = Forms.SystemInformation.VirtualScreen;
            return new Point(
                (point.X - bounds.Left) * ActualWidth / _screen.PixelWidth,
                (point.Y - bounds.Top) * ActualHeight / _screen.PixelHeight);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        CompleteCopy();
    }

    private void QuickAnnotation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tool }) return;
        if (_annotationMode)
        {
            CaptureInlineEditor.ActivateConfiguredTool(tool);
            SetCaptureAnnotationActiveTool(tool);
            CaptureInlineEditor.Focus();
            return;
        }
        BeginInlineAnnotation(tool);
    }

    private void Recapture_Click(object sender, RoutedEventArgs e)
    {
        _ocrCancellation?.Cancel();
        OcrPanel.Visibility = Visibility.Collapsed;
        _annotationHistoryRecordId = null;
        _ocrHistoryRecordId = null;
        if (_annotationMode)
        {
            _annotationMode = false;
            CaptureInlineEditor.Visibility = Visibility.Collapsed;
            SetCaptureAnnotationActiveTool(null);
            UpdateCaptureAnnotationHistoryButtons();
        }
        ResetSelection();
        Focus();
    }

    private void BeginInlineAnnotation(string? initialTool = null)
    {
        if (_annotationMode || !EnsureSelection()) return;
        // Preserve the user's existing toolbar anchor when annotation starts.
        // Every primary command already occupies a permanent slot, so opening
        // the property row must not alter the toolbar's size or position.
        PositionActionBar();
        var toolbarLeft = Canvas.GetLeft(ActionBar);
        var toolbarTop = Canvas.GetTop(ActionBar);
        _annotationMode = true;
        _ocrCancellation?.Cancel();
        OcrPanel.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Visible;
        ActionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var actionWidth = ActionBar.DesiredSize.Width;
        var actionHeight = ActionBar.DesiredSize.Height;
        Canvas.SetLeft(ActionBar, Math.Clamp(toolbarLeft, 8, Math.Max(8, ActualWidth - actionWidth - 8)));
        Canvas.SetTop(ActionBar, Math.Clamp(toolbarTop, 8, Math.Max(8, ActualHeight - actionHeight - 98)));
        var actionBounds = ActionBarBounds();
        SelectionRect.Fill = Brushes.Transparent;
        SelectionHandles.Visibility = Visibility.Visible;
        SelectionRect.Visibility = Visibility.Visible;
        DetectionRect.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
        ShortcutHints.Visibility = Visibility.Collapsed;
        CrossHorizontal.Visibility = Visibility.Collapsed;
        CrossVertical.Visibility = Visibility.Collapsed;
        CaptureInlineEditor.Visibility = Visibility.Visible;
        Canvas.SetLeft(CaptureInlineEditor, 0);
        Canvas.SetTop(CaptureInlineEditor, 0);
        CaptureInlineEditor.LoadImage(SelectedImage());
        CaptureInlineEditor.ConfigureCaptureOverlay(_selection, new Size(ActualWidth, ActualHeight), _selection,
            showActions: false, toolbarLeft: actionBounds.Left, toolbarTop: actionBounds.Bottom + 2,
            showPrimaryToolbar: false);
        UpdateCaptureAnnotationHistoryButtons();
        RenderSelection();
        initialTool ??= CaptureAnnotationToolPanel.Children.OfType<Button>()
            .FirstOrDefault(button => button.Visibility == Visibility.Visible && button.Tag is string)?.Tag as string;
        if (!string.IsNullOrWhiteSpace(initialTool))
        {
            CaptureInlineEditor.ActivateConfiguredTool(initialTool);
            SetCaptureAnnotationActiveTool(initialTool);
        }
        CaptureInlineEditor.Focus();
    }

    private void SetCaptureAnnotationActiveTool(string? activeTool)
    {
        foreach (var button in CaptureAnnotationToolPanel.Children.OfType<Button>())
        {
            var isActive = activeTool is not null &&
                string.Equals(button.Tag as string, activeTool, StringComparison.OrdinalIgnoreCase);
            button.Background = isActive
                ? new SolidColorBrush(Color.FromRgb(197, 230, 247))
                : Brushes.Transparent;
        }
    }

    private void AnnotationUndo_Click(object sender, RoutedEventArgs e)
    {
        CaptureInlineEditor.UndoForCapture();
        CaptureInlineEditor.Focus();
    }

    private void AnnotationRedo_Click(object sender, RoutedEventArgs e)
    {
        CaptureInlineEditor.RedoForCapture();
        CaptureInlineEditor.Focus();
    }

    private void UpdateCaptureAnnotationHistoryButtons()
    {
        // These slots never appear or disappear. They are muted while the user
        // is only choosing a capture, then become available as soon as an
        // annotation tool opens (a no-history click simply has no effect).
        CaptureUndoButton.IsEnabled = _annotationMode;
        CaptureRedoButton.IsEnabled = _annotationMode;
    }

    private void ExitInlineAnnotation()
    {
        if (!_annotationMode) return;
        if (_completionMode == CaptureCompletionMode.FullScreenDraw)
        {
            Close();
            return;
        }
        _annotationMode = false;
        CaptureInlineEditor.Visibility = Visibility.Collapsed;
        SetCaptureAnnotationActiveTool(null);
        UpdateCaptureAnnotationHistoryButtons();
        SelectionRect.Fill = new SolidColorBrush(Color.FromArgb(5, 255, 255, 255));
        SelectionRect.Visibility = Visibility.Visible;
        SelectionHandles.Visibility = Visibility.Visible;
        ShortcutHints.Visibility = CaptureHintsVisibility;
        CrossHorizontal.Visibility = _settings.ShowCrossLines ? Visibility.Visible : Visibility.Collapsed;
        CrossVertical.Visibility = _settings.ShowCrossLines ? Visibility.Visible : Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Visible;
        RenderSelection();
        PositionActionBar();
        Focus();
    }

    private void StoreCaptureAnnotation(AnnotationAppliedEventArgs document)
    {
        if (string.IsNullOrWhiteSpace(_annotationHistoryRecordId))
            _annotationHistoryRecordId = HistoryService.Add(document.BaseImage, SelectedPixelRegion(), _screen, "Edited").Id;
        HistoryService.SaveAnnotationDocument(_annotationHistoryRecordId, document.BaseImage, document.FlattenedImage, document.Items);
    }

    private void ApplyCaptureAnnotation(AnnotationAppliedEventArgs document)
    {
        StoreCaptureAnnotation(document);
        if (_annotationHistoryRecordId is { } id) HistoryService.UpdateSourceKind(id, "Edited");
        HistoryService.RememberLastRegion(SelectedPixelRegion());
        AutoSave(document.FlattenedImage);
        Close();
    }

    private BitmapSource CurrentResultImage(out bool annotationStored)
    {
        annotationStored = false;
        if (!_annotationMode) return SelectedImage();
        var document = CaptureInlineEditor.SnapshotDocument();
        StoreCaptureAnnotation(document);
        annotationStored = true;
        return document.FlattenedImage;
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => PinSelection(asThumbnail: false);

    private void PinThumbnail_Click(object sender, RoutedEventArgs e) => PinSelection(asThumbnail: true);

    private void PinSelection(bool asThumbnail)
    {
        var image = CurrentResultImage(out var annotationStored);
        Clipboard.SetImage(image);
        string? historyRecordId;
        if (!annotationStored)
            historyRecordId = HistoryService.Add(image, SelectedPixelRegion(), _screen, "Pinned").Id;
        else
        {
            historyRecordId = _annotationHistoryRecordId;
            if (historyRecordId is { } annotatedId) HistoryService.UpdateSourceKind(annotatedId, "Pinned");
        }
        HistoryService.RememberLastRegion(SelectedPixelRegion());
        AutoSave(image);
        var pixelRegion = SelectedPixelRegion();
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        var pinBounds = new System.Drawing.Rectangle(
            virtualBounds.Left + pixelRegion.X,
            virtualBounds.Top + pixelRegion.Y,
            pixelRegion.Width,
            pixelRegion.Height);
        Close();
        var pin = new PinnedImageWindow(image, historyRecordId: historyRecordId);
        pin.Show();
        pin.SetScreenPixelBounds(pinBounds);
        if (asThumbnail) pin.SetThumbnailMode(true);
    }

    private void CycleRegionHistory(int direction)
    {
        if (_regionHistory.Count == 0) return;
        _regionHistoryIndex = _regionHistoryIndex < 0
            ? direction >= 0 ? 0 : _regionHistory.Count - 1
            : (_regionHistoryIndex + direction + _regionHistory.Count) % _regionHistory.Count;
        var region = _regionHistory[_regionHistoryIndex];
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        var topLeft = ScreenToOverlay(new Point(virtualBounds.Left + region.X, virtualBounds.Top + region.Y));
        var bottomRight = ScreenToOverlay(new Point(virtualBounds.Left + region.X + region.Width, virtualBounds.Top + region.Y + region.Height));
        _selection = Rect.Intersect(new Rect(0, 0, ActualWidth, ActualHeight), new Rect(topLeft, bottomRight));
        if (_selection.Width < 3 || _selection.Height < 3) return;
        _annotationHistoryRecordId = null;
        _ocrHistoryRecordId = null;
        RenderSelection();
        PositionActionBar();
        ActionBar.Visibility = Visibility.Visible;
    }

    private async void Recognize_Click(object sender, RoutedEventArgs e) => await RunCaptureOcrAsync(_settings.OcrAutoCopy);

    private async void LongCapture_Click(object sender, RoutedEventArgs e) => await RunLongCaptureAsync();

    private async void Record_Click(object sender, RoutedEventArgs e) => await RunRecordingAsync();

    private async Task RunRecordingAsync()
    {
        if (_recordingRunning || !EnsureSelection()) return;
        _recordingRunning = true;
        _recordingPaused = false;
        RecordingPauseIcon.Visibility = Visibility.Visible;
        RecordingResumeIcon.Visibility = Visibility.Collapsed;
        RecordingPauseLabel.Text = L("Pause");
        RecordingPauseButton.ToolTip = "Pause recording (Space)";
        _recordingStopRequested = false;
        _recordingCancellation = new CancellationTokenSource();
        _settings.RecordingIncludeCursor = RecordingCursorBox.IsChecked ?? _settings.RecordingIncludeCursor;
        SettingsService.Save(_settings);
        var pixelRegion = SelectedPixelRegion();
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        var screenRegion = new Rect(virtualBounds.Left + pixelRegion.X, virtualBounds.Top + pixelRegion.Y, pixelRegion.Width, pixelRegion.Height);
        _recordingTargetWindow = _settings.RecordingCaptureMode.Equals("Window", StringComparison.OrdinalIgnoreCase)
            ? ElementDetectionService.WindowHandleAt(new Point(screenRegion.X + screenRegion.Width / 2, screenRegion.Y + screenRegion.Height / 2), _overlayHandle)
            : IntPtr.Zero;

        EnterRecordingAppearance();
        try
        {
            var countdown = Math.Clamp(_settings.RecordingCountdownSeconds, 0, 10);
            for (var remaining = countdown; remaining > 0; remaining--)
            {
                RecordingCountdownText.Text = remaining.ToString();
                RecordingCountdownBadge.Visibility = Visibility.Visible;
                await Task.Delay(1000, _recordingCancellation.Token);
                if (_recordingStopRequested)
                {
                    RestoreCaptureAppearance();
                    return;
                }
            }
            RecordingCountdownBadge.Visibility = Visibility.Collapsed;
            _settings.RecordingIncludeCursor = RecordingCursorBox.IsChecked == true;
            RecordingTimerText.Text = "00:00";
            var result = _settings.RecordingFormat.Equals("MP4", StringComparison.OrdinalIgnoreCase)
                ? await CaptureMp4Async(screenRegion, _recordingCancellation.Token)
                : await ScreenRecordingService.CaptureGifAsync(
                    screenRegion,
                    _settings,
                    () => _recordingPaused,
                    () => _recordingStopRequested,
                    (elapsed, frames) => Dispatcher.BeginInvoke(() =>
                    {
                        RecordingTimerText.Text = elapsed.ToString(@"mm\:ss");
                        RecordingFrameText.Text = _recordingPaused ? L("Paused") : frames == 1 ? L("1 frame") : LocalizationService.Format("{0} frames", frames);
                    }),
                    _recordingCancellation.Token);
            if (_settings.RecordingFormat.Equals("MP4", StringComparison.OrdinalIgnoreCase))
            {
                var reviewed = await ReviewAndTrimAsync(result, _recordingCancellation.Token);
                if (reviewed is null)
                {
                    RestoreCaptureAppearance();
                    return;
                }
                result = reviewed;
            }
            RecordingTimerText.Text = L("Saving…");
            HistoryService.AddRecording(result.Preview, result.FilePath, result.Duration, result.FrameCount);
            System.Media.SystemSounds.Asterisk.Play();
            Close();
        }
        catch (OperationCanceledException)
        {
            if (IsVisible) RestoreCaptureAppearance();
        }
        catch (Exception ex)
        {
            RestoreCaptureAppearance();
            MessageBox.Show(this, ex.Message, L("Recording failed"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            _advancedRecording = null;
            _recordingReviewRunning = false;
            _recordingRunning = false;
            _recordingPaused = false;
        }
    }

    private async Task<ScreenRecordingResult> CaptureMp4Async(Rect screenRegion, CancellationToken cancellationToken)
    {
        using var session = AdvancedRecordingSession.Start(screenRegion, _settings, _overlayHandle, _recordingTargetWindow);
        _advancedRecording = session;
        RecordingFrameText.Text = $"MP4 · {_settings.RecordingCaptureMode}";
        var maximum = TimeSpan.FromSeconds(Math.Clamp(_settings.RecordingMaxDurationSeconds, 5, 600));
        try
        {
            while (!_recordingStopRequested && session.Elapsed < maximum)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RecordingTimerText.Text = session.Elapsed.ToString(@"mm\:ss");
                if (!_recordingPaused) RecordingFrameText.Text = $"MP4 · {_settings.RecordingCaptureMode} · {session.FrameCount}f";
                await Task.Delay(100, cancellationToken);
            }
            RecordingTimerText.Text = L("Saving…");
            var result = await session.StopAsync(cancellationToken);
            return new ScreenRecordingResult(result.FilePath, result.Preview, result.Duration, result.FrameCount);
        }
        catch (OperationCanceledException)
        {
            try { await session.StopAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private void EnterRecordingAppearance()
    {
        ScreenImage.Visibility = Visibility.Collapsed;
        MaskLayer.Visibility = Visibility.Collapsed;
        SelectionMask.Visibility = Visibility.Collapsed;
        SelectionHandles.Visibility = Visibility.Collapsed;
        DetectionRect.Visibility = Visibility.Collapsed;
        CrossHorizontal.Visibility = Visibility.Collapsed;
        CrossVertical.Visibility = Visibility.Collapsed;
        ShortcutHints.Visibility = Visibility.Collapsed;
        SizeBadge.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        CaptureInlineEditor.Visibility = Visibility.Collapsed;
        OcrPanel.Visibility = Visibility.Collapsed;
        Root.Background = Brushes.Transparent;
        Background = Brushes.Transparent;
        SelectionRect.Stroke = Brushes.Red;
        SelectionRect.Fill = Brushes.Transparent;
        RecordingCursorBox.IsChecked = _settings.RecordingIncludeCursor;
        RecordingBar.Visibility = Visibility.Visible;
        PositionRecordingControls();
        Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private void RestoreCaptureAppearance()
    {
        ScreenImage.Visibility = Visibility.Visible;
        Root.Background = Brushes.Black;
        Background = Brushes.Black;
        RecordingBar.Visibility = Visibility.Collapsed;
        RecordingCountdownBadge.Visibility = Visibility.Collapsed;
        ShortcutHints.Visibility = CaptureHintsVisibility;
        SelectionRect.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_settings.CaptureBorderColor));
        Cursor = System.Windows.Input.Cursors.Cross;
        RenderSelection();
        CaptureInlineEditor.Visibility = _annotationMode ? Visibility.Visible : Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Visible;
        PositionActionBar();
    }

    private void PositionRecordingControls()
    {
        var compact = ActualWidth < 520;
        RecordingFrameText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RecordingCursorBox.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        RecordingBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = RecordingBar.DesiredSize.Width;
        var height = RecordingBar.DesiredSize.Height;
        var left = Math.Clamp(_selection.Right - width, 8, Math.Max(8, ActualWidth - width - 8));
        var top = _selection.Bottom + 10 + height <= ActualHeight ? _selection.Bottom + 10 : Math.Max(8, _selection.Top - height - 10);
        Canvas.SetLeft(RecordingBar, left);
        Canvas.SetTop(RecordingBar, top);
        Canvas.SetLeft(RecordingCountdownBadge, Math.Clamp(_selection.X + (_selection.Width - 82) / 2, 8, Math.Max(8, ActualWidth - 90)));
        Canvas.SetTop(RecordingCountdownBadge, Math.Clamp(_selection.Y + (_selection.Height - 82) / 2, 8, Math.Max(8, ActualHeight - 90)));
        RecordingReviewPanel.Width = Math.Min(620, Math.Max(300, ActualWidth - 20));
        RecordingReviewPanel.Height = Math.Min(470, Math.Max(260, ActualHeight - 20));
        if (!_recordingReviewPositioned)
        {
            Canvas.SetLeft(RecordingReviewPanel, Math.Max(10, (ActualWidth - RecordingReviewPanel.Width) / 2));
            Canvas.SetTop(RecordingReviewPanel, Math.Max(10, (ActualHeight - RecordingReviewPanel.Height) / 2));
            _recordingReviewPositioned = true;
        }
        else
        {
            Canvas.SetLeft(RecordingReviewPanel, Math.Clamp(Canvas.GetLeft(RecordingReviewPanel), 0, Math.Max(0, ActualWidth - RecordingReviewPanel.Width)));
            Canvas.SetTop(RecordingReviewPanel, Math.Clamp(Canvas.GetTop(RecordingReviewPanel), 0, Math.Max(0, ActualHeight - RecordingReviewPanel.Height)));
        }
    }

    private void RecordingReviewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_recordingReviewRunning) return;
        _recordingReviewDragging = true;
        _recordingReviewDragStart = e.GetPosition(OverlayCanvas);
        _recordingReviewStart = new Point(Canvas.GetLeft(RecordingReviewPanel), Canvas.GetTop(RecordingReviewPanel));
        RecordingReviewHeader.CaptureMouse();
        e.Handled = true;
    }

    private void RecordingReviewHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_recordingReviewDragging) return;
        var point = e.GetPosition(OverlayCanvas);
        var left = _recordingReviewStart.X + point.X - _recordingReviewDragStart.X;
        var top = _recordingReviewStart.Y + point.Y - _recordingReviewDragStart.Y;
        Canvas.SetLeft(RecordingReviewPanel, Math.Clamp(left, 0, Math.Max(0, ActualWidth - RecordingReviewPanel.ActualWidth)));
        Canvas.SetTop(RecordingReviewPanel, Math.Clamp(top, 0, Math.Max(0, ActualHeight - RecordingReviewPanel.ActualHeight)));
        e.Handled = true;
    }

    private void RecordingReviewHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_recordingReviewDragging) return;
        _recordingReviewDragging = false;
        RecordingReviewHeader.ReleaseMouseCapture();
        e.Handled = true;
    }

    private async Task<ScreenRecordingResult?> ReviewAndTrimAsync(ScreenRecordingResult result, CancellationToken cancellationToken)
    {
        if (result.Duration < TimeSpan.FromSeconds(1)) return result;
        _recordingReviewRunning = true;
        RecordingBar.Visibility = Visibility.Collapsed;
        RecordingCountdownBadge.Visibility = Visibility.Collapsed;
        SelectionRect.Visibility = Visibility.Collapsed;
        TrimStartSlider.Maximum = result.Duration.TotalSeconds;
        TrimEndSlider.Maximum = result.Duration.TotalSeconds;
        TrimStartSlider.Value = 0;
        TrimEndSlider.Value = result.Duration.TotalSeconds;
        TrimDurationText.Text = result.Duration.ToString(@"mm\:ss");
        RecordingSavePathText.Text = LocalizationService.Format("Will save to: {0}", result.FilePath);
        RecordingSavePathText.ToolTip = result.FilePath;
        _recordingPreviewPlaying = false;
        ReviewPlayIcon.Visibility = Visibility.Visible;
        ReviewPauseIcon.Visibility = Visibility.Collapsed;
        ReviewPlayLabel.Text = L("Play");
        _recordingReviewPositioned = false;
        RecordingPreviewMedia.Source = new Uri(result.FilePath);
        RecordingReviewPanel.Visibility = Visibility.Visible;
        PositionRecordingControls();
        UpdateTrimLabels();
        _recordingReviewChoice = new TaskCompletionSource<RecordingReviewChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        var choice = await _recordingReviewChoice.Task.WaitAsync(cancellationToken);
        RecordingPreviewMedia.Stop();
        RecordingPreviewMedia.Source = null;
        RecordingPreviewMedia.Close();
        RecordingReviewPanel.Visibility = Visibility.Collapsed;
        _recordingReviewDragging = false;
        _recordingReviewRunning = false;
        if (choice == RecordingReviewChoice.Discard)
        {
            for (var attempt = 0; attempt < 5 && File.Exists(result.FilePath); attempt++)
            {
                try { File.Delete(result.FilePath); }
                catch (IOException) when (attempt < 4) { await Task.Delay(120, cancellationToken); }
            }
            if (File.Exists(result.FilePath))
            {
                DiagnosticsService.Log("recording-discard", $"Could not delete discarded recording: {result.FilePath}");
                MessageBox.Show(this, $"The recording was not added to history, but Windows kept the temporary file open:\n\n{result.FilePath}",
                    "Recording discarded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return null;
        }
        if (choice == RecordingReviewChoice.KeepFull) return result;
        var start = TimeSpan.FromSeconds(TrimStartSlider.Value);
        var end = TimeSpan.FromSeconds(TrimEndSlider.Value);
        var endTrim = result.Duration - end;
        RecordingTimerText.Text = L("Trimming…");
        var path = await MediaTrimService.TrimMp4Async(result.FilePath, start, endTrim, cancellationToken);
        return result with { FilePath = path, Duration = end - start };
    }

    private void TrimSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_initialized || TrimStartText is null) return;
        if (TrimEndSlider.Value < TrimStartSlider.Value + 0.25)
        {
            if (ReferenceEquals(sender, TrimStartSlider)) TrimEndSlider.Value = Math.Min(TrimEndSlider.Maximum, TrimStartSlider.Value + 0.25);
            else TrimStartSlider.Value = Math.Max(0, TrimEndSlider.Value - 0.25);
        }
        UpdateTrimLabels();
    }

    private void UpdateTrimLabels()
    {
        TrimStartText.Text = TimeSpan.FromSeconds(TrimStartSlider.Value).ToString(@"mm\:ss");
        TrimEndText.Text = TimeSpan.FromSeconds(TrimEndSlider.Value).ToString(@"mm\:ss");
    }

    private void RecordingPreviewPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingPreviewPlaying) RecordingPreviewMedia.Pause();
        else
        {
            RecordingPreviewMedia.Position = TimeSpan.FromSeconds(TrimStartSlider.Value);
            RecordingPreviewMedia.Play();
        }
        _recordingPreviewPlaying = !_recordingPreviewPlaying;
        ReviewPlayIcon.Visibility = _recordingPreviewPlaying ? Visibility.Collapsed : Visibility.Visible;
        ReviewPauseIcon.Visibility = _recordingPreviewPlaying ? Visibility.Visible : Visibility.Collapsed;
        ReviewPlayLabel.Text = L(_recordingPreviewPlaying ? "Pause" : "Play");
    }

    private void RecordingPreviewMedia_MediaEnded(object sender, RoutedEventArgs e)
    {
        _recordingPreviewPlaying = false;
        RecordingPreviewMedia.Stop();
        RecordingPreviewMedia.Position = TimeSpan.FromSeconds(TrimStartSlider.Value);
        ReviewPlayIcon.Visibility = Visibility.Visible;
        ReviewPauseIcon.Visibility = Visibility.Collapsed;
        ReviewPlayLabel.Text = L("Play");
    }

    private void KeepFullRecording_Click(object sender, RoutedEventArgs e) =>
        _recordingReviewChoice?.TrySetResult(RecordingReviewChoice.KeepFull);

    private void SaveTrimmedRecording_Click(object sender, RoutedEventArgs e) =>
        _recordingReviewChoice?.TrySetResult(RecordingReviewChoice.SaveTrimmed);

    private void DiscardRecording_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, L("Discard this recording permanently?"), L("Discard recording"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            _recordingReviewChoice?.TrySetResult(RecordingReviewChoice.Discard);
    }

    private void RecordingPause_Click(object sender, RoutedEventArgs e)
    {
        if (!_recordingRunning) return;
        _recordingPaused = !_recordingPaused;
        if (_advancedRecording is not null)
        {
            if (_recordingPaused) _advancedRecording.Pause(); else _advancedRecording.Resume();
        }
        RecordingPauseIcon.Visibility = _recordingPaused ? Visibility.Collapsed : Visibility.Visible;
        RecordingResumeIcon.Visibility = _recordingPaused ? Visibility.Visible : Visibility.Collapsed;
        RecordingPauseLabel.Text = L(_recordingPaused ? "Resume" : "Pause");
        RecordingPauseButton.ToolTip = _recordingPaused ? "Resume recording (Space)" : "Pause recording (Space)";
        RecordingFrameText.Text = L(_recordingPaused ? "Paused" : "Resuming…");
    }

    private void RecordingStop_Click(object sender, RoutedEventArgs e)
    {
        if (_recordingRunning) _recordingStopRequested = true;
    }

    private async Task RunLongCaptureAsync()
    {
        if (_longCaptureRunning || !EnsureSelection()) return;
        _longCaptureRunning = true;
        _ocrCancellation?.Cancel();
        var pixelRegion = SelectedPixelRegion();
        var virtualBounds = Forms.SystemInformation.VirtualScreen;
        var screenRegion = new Rect(
            virtualBounds.Left + pixelRegion.X,
            virtualBounds.Top + pixelRegion.Y,
            pixelRegion.Width,
            pixelRegion.Height);
        var centerX = (int)Math.Round(screenRegion.X + screenRegion.Width / 2);
        var centerY = (int)Math.Round(screenRegion.Y + screenRegion.Height / 2);
        var originalCursor = Forms.Cursor.Position;
        LongCaptureProgressWindow? progressWindow = null;
        var stopRequested = false;

        try
        {
            Hide();
            await Task.Delay(220);
            NativeMethods.SetCursorPos(centerX, centerY);
            var target = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint { X = centerX, Y = centerY });
            if (target != IntPtr.Zero)
            {
                var root = NativeMethods.GetAncestor(target, NativeMethods.GaRoot);
                NativeMethods.SetForegroundWindow(root == IntPtr.Zero ? target : root);
            }
            await Task.Delay(140);
            progressWindow = new LongCaptureProgressWindow();
            progressWindow.StopRequested += (_, _) => stopRequested = true;
            progressWindow.Show();
            var progress = new Progress<ScrollingCaptureProgress>(item => progressWindow?.Report(item));
            var result = await ScrollingCaptureService.CaptureAsync(
                screenRegion,
                _settings,
                progress: progress,
                stopRequested: () => stopRequested);
            progressWindow.Close();
            progressWindow = null;
            NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
            Close();
            Clipboard.SetImage(result.Image);
            HistoryService.Add(result.Image, sourceKind: "Scrolling");
            var pin = new PinnedImageWindow(result.Image);
            pin.Show();
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (OperationCanceledException)
        {
            NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
            Show(); Activate();
        }
        catch (Exception ex)
        {
            NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
            Show(); Activate();
            MessageBox.Show(this, ex.Message, L("Long screenshot failed"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        finally
        {
            progressWindow?.Close();
            _longCaptureRunning = false;
        }
    }

    private async Task RunCaptureOcrAsync(bool copyWhenDone)
    {
        if (!EnsureSelection()) return;
        _ocrCancellation?.Cancel();
        _ocrCancellation = new CancellationTokenSource();
        OcrPanel.Visibility = Visibility.Visible;
        CaptureOcrResultBox.Text = L("Recognizing locally…");
        CopyOcrButton.IsEnabled = false;
        RetryOcrButton.IsEnabled = false;
        ActionBar.Visibility = Visibility.Visible;
        PositionFloatingPanels();

        try
        {
            var ocrArea = _ocrAreaSelection.Width >= 3 && _ocrAreaSelection.Height >= 3
                ? _ocrAreaSelection
                : _selection;
            var pixelRegion = PixelRegion(ocrArea);
            var image = CaptureService.Crop(_screen, pixelRegion);
            var language = CaptureOcrLanguageBox.SelectedValue as string ?? "eng";
            var result = await RecognitionService.RecognizeAsync(image, language, _ocrCancellation.Token);
            var sections = new List<string>();
            if (!string.IsNullOrWhiteSpace(result.Text)) sections.Add(result.Text.Trim());
            if (!string.IsNullOrWhiteSpace(result.BarcodeText))
                sections.Add($"[{(string.IsNullOrWhiteSpace(result.BarcodeFormat) ? "CODE" : result.BarcodeFormat)}]{Environment.NewLine}{result.BarcodeText.Trim()}");
            CaptureOcrResultBox.Text = sections.Count > 0 ? string.Join($"{Environment.NewLine}{Environment.NewLine}", sections) : L("No text or code was found.");
            CopyOcrButton.IsEnabled = sections.Count > 0;

            if (sections.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(_ocrHistoryRecordId))
                    _ocrHistoryRecordId = HistoryService.Add(image, pixelRegion, _screen, "OCR").Id;
                HistoryService.UpdateRecognition(_ocrHistoryRecordId, result.Text, result.BarcodeText, result.BarcodeFormat);
                if (copyWhenDone || OcrAutoCopyBox.IsChecked == true) Clipboard.SetText(CaptureOcrResultBox.Text);
            }
        }
        catch (OperationCanceledException)
        {
            CaptureOcrResultBox.Text = L("Recognition cancelled.");
        }
        catch (Exception ex)
        {
            while (ex.InnerException is not null) ex = ex.InnerException;
            CaptureOcrResultBox.Text = LocalizationService.Format("Recognition failed: {0}", ex.Message);
        }
        finally
        {
            RetryOcrButton.IsEnabled = true;
        }
    }

    private void SelectOcrArea_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelection()) return;
        _ocrCancellation?.Cancel();
        _ocrAreaSelectionMode = true;
        _ocrAreaDragging = false;
        _ocrAreaSelection = Rect.Empty;
        OcrAreaRect.Visibility = Visibility.Collapsed;
        OcrAreaHint.Visibility = Visibility.Visible;
        SelectionHandles.Visibility = Visibility.Collapsed;
        PositionOcrAreaHint();
        Focus();
        Cursor = Cursors.Cross;
    }

    private void BeginOcrAreaDrag(Point point)
    {
        var clamped = ClampToSelection(point);
        _ocrAreaStart = clamped;
        _ocrAreaSelection = new Rect(clamped, clamped);
        _ocrAreaDragging = true;
        CaptureMouse();
        UpdateOcrAreaRect();
    }

    private void UpdateOcrAreaDrag(Point point)
    {
        var clamped = ClampToSelection(point);
        _ocrAreaSelection = OcrAreaWithin(_selection, _ocrAreaStart, clamped);
        UpdateOcrAreaRect();
    }

    private void FinishOcrAreaDrag(Point point)
    {
        if (!_ocrAreaDragging) return;
        UpdateOcrAreaDrag(point);
        _ocrAreaDragging = false;
        _ocrAreaSelectionMode = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        OcrAreaHint.Visibility = Visibility.Collapsed;
        if (_ocrAreaSelection.Width < 3 || _ocrAreaSelection.Height < 3)
        {
            _ocrAreaSelection = Rect.Empty;
            OcrAreaRect.Visibility = Visibility.Collapsed;
            CaptureOcrResultBox.Text = L("The recognition area was too small. Select an area and try again.");
            return;
        }
        _ocrHistoryRecordId = null;
        _ = RunCaptureOcrAsync(false);
    }

    internal static Rect OcrAreaWithin(Rect selection, Point start, Point end)
        => Rect.Intersect(new Rect(start, end), selection);

    private Point ClampToSelection(Point point)
        => new(Math.Clamp(point.X, _selection.Left, _selection.Right),
            Math.Clamp(point.Y, _selection.Top, _selection.Bottom));

    private void UpdateOcrAreaRect()
    {
        if (_ocrAreaSelection.IsEmpty) return;
        Canvas.SetLeft(OcrAreaRect, _ocrAreaSelection.Left);
        Canvas.SetTop(OcrAreaRect, _ocrAreaSelection.Top);
        OcrAreaRect.Width = _ocrAreaSelection.Width;
        OcrAreaRect.Height = _ocrAreaSelection.Height;
        OcrAreaRect.Visibility = Visibility.Visible;
    }

    private void PositionOcrAreaHint()
    {
        OcrAreaHint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = OcrAreaHint.DesiredSize.Width;
        var height = OcrAreaHint.DesiredSize.Height;
        var left = Math.Clamp(_selection.Left + (_selection.Width - width) / 2, 8, Math.Max(8, ActualWidth - width - 8));
        var top = Math.Clamp(_selection.Top + 10, 8, Math.Max(8, ActualHeight - height - 8));
        Canvas.SetLeft(OcrAreaHint, left);
        Canvas.SetTop(OcrAreaHint, top);
    }

    private void CancelOcrAreaSelection()
    {
        _ocrAreaSelectionMode = false;
        _ocrAreaDragging = false;
        ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        OcrAreaHint.Visibility = Visibility.Collapsed;
        if (_ocrAreaSelection.Width < 3 || _ocrAreaSelection.Height < 3)
        {
            _ocrAreaSelection = Rect.Empty;
            OcrAreaRect.Visibility = Visibility.Collapsed;
        }
        Focus();
    }

    private void RetryOcr_Click(object sender, RoutedEventArgs e)
    {
        _ocrHistoryRecordId = null;
        _ = RunCaptureOcrAsync(false);
    }

    private bool EnsureSelection()
    {
        if (_selection.Width >= 3 && _selection.Height >= 3) return true;
        if (_detectedSelection.Width < 3 || _detectedSelection.Height < 3) return false;
        _selection = _detectedSelection;
        RenderSelection();
        PositionActionBar();
        ActionBar.Visibility = Visibility.Visible;
        return true;
    }

    private void CloseOcr_Click(object sender, RoutedEventArgs e)
    {
        _ocrCancellation?.Cancel();
        CancelOcrAreaSelection();
        _ocrAreaSelection = Rect.Empty;
        OcrAreaRect.Visibility = Visibility.Collapsed;
        SelectionHandles.Visibility = _selection.Width >= 3 && _selection.Height >= 3 ? Visibility.Visible : Visibility.Collapsed;
        OcrPanel.Visibility = Visibility.Collapsed;
        Focus();
    }

    private void CopyOcr_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CaptureOcrResultBox.Text)) Clipboard.SetText(CaptureOcrResultBox.Text);
    }

    private void OcrLayout_Click(object sender, RoutedEventArgs e)
    {
        _ocrLayoutMode = !_ocrLayoutMode;
        CaptureOcrResultBox.TextWrapping = _ocrLayoutMode ? TextWrapping.NoWrap : TextWrapping.Wrap;
        CaptureOcrResultBox.FontFamily = new FontFamily(_ocrLayoutMode ? "Consolas" : "Segoe UI");
        OcrLayoutButton.FontWeight = _ocrLayoutMode ? FontWeights.Bold : FontWeights.Normal;
    }

    private void CaptureOcrLanguage_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized) return;
        _settings.OcrLanguage = CaptureOcrLanguageBox.SelectedValue as string ?? "eng";
        SettingsService.Save(_settings);
    }

    private void OcrAutoCopy_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;
        _settings.OcrAutoCopy = OcrAutoCopyBox.IsChecked == true;
        SettingsService.Save(_settings);
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
        if (dialog.ShowDialog(this) != true) return;
        var image = CurrentResultImage(out var annotationStored);
        CaptureService.SaveImage(image, dialog.FileName, _settings.ImageQuality, _settings);
        if (!annotationStored) HistoryService.Add(image, SelectedPixelRegion(), _screen, "Saved");
        else if (_annotationHistoryRecordId is { } annotatedId) HistoryService.UpdateSourceKind(annotatedId, "Saved");
        HistoryService.RememberLastRegion(SelectedPixelRegion());
        Close();
    }

    private void QuickSave_Click(object sender, RoutedEventArgs e)
    {
        var image = CurrentResultImage(out var annotationStored);
        var path = SettingsService.CreateOutputPath(_settings.QuickSaveFolder, _settings.OutputFileName);
        CaptureService.SaveImage(image, path, _settings.ImageQuality, _settings);
        Clipboard.SetImage(image);
        if (!annotationStored) HistoryService.Add(image, SelectedPixelRegion(), _screen, "Saved");
        else if (_annotationHistoryRecordId is { } annotatedId) HistoryService.UpdateSourceKind(annotatedId, "Saved");
        HistoryService.RememberLastRegion(SelectedPixelRegion());
        if (_settings.ShowSaveNotification) System.Media.SystemSounds.Asterisk.Play();
        Close();
    }

    private void CompleteCopy()
    {
        var image = CurrentResultImage(out var annotationStored);
        Clipboard.SetImage(image);
        if (!annotationStored) HistoryService.Add(image, SelectedPixelRegion(), _screen, "Copied");
        else if (_annotationHistoryRecordId is { } annotatedId) HistoryService.UpdateSourceKind(annotatedId, "Copied");
        HistoryService.RememberLastRegion(SelectedPixelRegion());
        AutoSave(image);
        Close();
    }

    private void AutoSave(BitmapSource image)
    {
        if (!_settings.AutoSave || string.IsNullOrWhiteSpace(_settings.AutoSaveFolder)) return;
        CaptureService.SaveImage(image, SettingsService.CreateOutputPath(_settings.AutoSaveFolder, _settings.OutputFileName), _settings.ImageQuality, _settings);
    }

    private static void TrySetBrush(DependencyObject target, DependencyProperty property, string value)
    {
        try
        {
            if (new BrushConverter().ConvertFromString(value) is Brush brush) target.SetValue(property, brush);
        }
        catch
        {
            // Keep the built-in color if the custom value is invalid.
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_ocrAreaSelectionMode)
        {
            if (e.Key == Key.Escape) CancelOcrAreaSelection();
            e.Handled = true;
            return;
        }
        if (_annotationMode)
        {
            if (e.Key == Key.Escape) Close();
            e.Handled = true;
            return;
        }
        if (_recordingRunning)
        {
            if (_recordingReviewRunning)
            {
                if (e.Key == Key.Space) RecordingPreviewPlay_Click(this, new RoutedEventArgs());
                else if (e.Key == Key.Escape) DiscardRecording_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Space) RecordingPause_Click(this, new RoutedEventArgs());
            else if (e.Key is Key.Escape or Key.R) RecordingStop_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            if (OcrPanel.Visibility == Visibility.Visible) CloseOcr_Click(this, new RoutedEventArgs()); else Close();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Tab)
        {
            if (_settings.ShowElementDetection == false)
            {
                DetectionRect.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }
            _detectElements = !_detectElements;
            if (_detectionCandidates.Count > 0)
            {
                _detectionIndex = _detectElements ? _detectionCandidates.Count - 1 : 0;
                RenderDetectedCandidate();
            }
            DetectionModeText.Text = $"{L("Mode")}: {L(_detectElements ? "UI element" : "window")}";
            e.Handled = true;
            return;
        }
        if (e.Key is Key.OemComma or Key.OemPeriod)
        {
            CycleRegionHistory(e.Key == Key.OemComma ? 1 : -1);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.W or Key.A or Key.S or Key.D)
        {
            var cursor = Forms.Cursor.Position;
            var step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 10 : 1;
            if (e.Key == Key.W) cursor.Y -= step;
            if (e.Key == Key.S) cursor.Y += step;
            if (e.Key == Key.A) cursor.X -= step;
            if (e.Key == Key.D) cursor.X += step;
            NativeMethods.SetCursorPos(cursor.X, cursor.Y);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.C)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                _ = RunCaptureOcrAsync(true);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.L)
        {
            _ = RunLongCaptureAsync();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.R)
        {
            _ = RunRecordingAsync();
            e.Handled = true;
            return;
        }
        if (_selection.Width >= 3 && e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            var pixelX = ActualWidth / Math.Max(1, _screen.PixelWidth);
            var pixelY = ActualHeight / Math.Max(1, _screen.PixelHeight);
            var multiplier = (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? 10 : 1;
            pixelX *= multiplier;
            pixelY *= multiplier;
            var dx = e.Key == Key.Left ? -pixelX : e.Key == Key.Right ? pixelX : 0;
            var dy = e.Key == Key.Up ? -pixelY : e.Key == Key.Down ? pixelY : 0;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                // Ctrl adjusts the top/left edge while preserving the opposite edge.
                if (dx != 0)
                {
                    var right = _selection.Right;
                    _selection.X = Math.Clamp(_selection.X + dx, 0, right - pixelX);
                    _selection.Width = right - _selection.X;
                }
                if (dy != 0)
                {
                    var bottom = _selection.Bottom;
                    _selection.Y = Math.Clamp(_selection.Y + dy, 0, bottom - pixelY);
                    _selection.Height = bottom - _selection.Y;
                }
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                _selection.Width = Math.Max(pixelX, Math.Min(ActualWidth - _selection.X, _selection.Width + dx));
                _selection.Height = Math.Max(pixelY, Math.Min(ActualHeight - _selection.Y, _selection.Height + dy));
            }
            else
            {
                _selection.X = Math.Clamp(_selection.X + dx, 0, Math.Max(0, ActualWidth - _selection.Width));
                _selection.Y = Math.Clamp(_selection.Y + dy, 0, Math.Max(0, ActualHeight - _selection.Height));
            }
            RenderSelection();
            PositionActionBar();
            e.Handled = true;
        }
        if (e.Key == Key.Enter && _selection.Width >= 3)
        {
            Clipboard.SetImage(SelectedImage());
            Close();
        }
    }

    private void ResetSelection()
    {
        _selection = Rect.Empty;
        _detectedSelection = Rect.Empty;
        _detectionCandidates = [];
        SelectionRect.Visibility = Visibility.Collapsed;
        SelectionHandles.Visibility = Visibility.Collapsed;
        SelectionMask.Visibility = Visibility.Collapsed;
        MaskLayer.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Collapsed;
        ActionBar.Visibility = Visibility.Collapsed;
        DetectionRect.Visibility = Visibility.Collapsed;
    }

    private static string L(string value) => LocalizationService.Current(value);
}
