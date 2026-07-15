using Microsoft.Win32;
using SnapPin.Models;
using SnapPin.Services;
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

namespace SnapPin.Windows;

internal sealed record PinSnapshot(Guid Id, BitmapSource Image, string Group, bool IsVisible, bool IsOnCurrentDesktop, string SizeLabel)
{
    public string VisibilityLabel => !IsVisible ? "Hidden" : IsOnCurrentDesktop ? "Visible" : "Other desktop";
}

public partial class PinnedImageWindow : Window
{
    private static readonly HashSet<PinnedImageWindow> OpenPins = [];
    private static readonly HashSet<PinnedImageWindow> SelectedPins = [];
    private static bool _soloMode;
    public static event EventHandler? PinsChanged;

    private readonly Guid _pinId = Guid.NewGuid();
    private BitmapSource _baseSource;
    private BitmapSource _source;
    private readonly AppSettings _settings;
    private readonly bool _startEditing;
    private string _groupName;
    private string _backgroundMode;
    private string _inlineMode = "None";
    private string? _historyRecordId;
    private string _lastRecognitionFingerprint = string.Empty;
    private bool _grayscale;
    private bool _inverted;
    private bool _clickThrough;
    private bool _thumbnail;
    private bool _positionLocked;
    private bool _shaded;
    private bool _edgeSnapping = true;
    private double _preShadeHeight;
    private string _pinTitle = string.Empty;
    private readonly bool _longScrollable;
    private Rect _preThumbnailBounds;
    private double _normalHeight;
    private double _rotation;
    private double _flipX = 1;
    private double _flipY = 1;
    private readonly DispatcherTimer _zoomAnimationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _zoomBadgeTimer = new() { Interval = TimeSpan.FromMilliseconds(850) };
    private readonly DispatcherTimer _refreshTimer = new();
    private readonly DispatcherTimer _textAutoScrollTimer = new() { Interval = TimeSpan.FromMilliseconds(28) };
    private readonly DispatcherTimer _longScrollAnimationTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly BitmapCache _zoomBitmapCache = new() { RenderAtScale = 1 };
    private Rect _targetZoomBounds;
    private bool _zoomAnimating;
    private double _zoomReferenceWidth;
    private bool _shadowEnabled;
    private string _sourceFilePath = string.Empty;
    private CancellationTokenSource? _selectableTextCancellation;
    private BitmapSource? _recognizedTextSource;
    private RecognitionResult? _selectableTextResult;
    private IReadOnlyList<RecognizedWord> _recognizedWords = Array.Empty<RecognizedWord>();
    private readonly Dictionary<Canvas, List<Rect>> _textHitRegions = [];
    private bool _textSelectable;
    private bool _selectingText;
    private int _textAutoScrollDirection;
    private double _targetLongScrollOffset;
    private int? _textSelectionAnchor;
    private int? _textSelectionEnd;
    private Point? _textSelectionToolbarPosition;
    private readonly bool _enableSelectableTextOnLoad;

    public PinnedImageWindow(BitmapSource source, bool startEditing = false, string? groupName = null, string? backgroundMode = null,
        string? historyRecordId = null, bool applyTextSelectableDefault = true)
    {
        InitializeComponent();
        _settings = SettingsService.Load();
        _baseSource = _source = source;
        _startEditing = startEditing;
        _groupName = groupName ?? _settings.CurrentPinGroup;
        _backgroundMode = backgroundMode ?? _settings.DefaultPinBackground;
        _pinTitle = "Pinned image";
        Title = $"SnapPin - {_pinTitle}";
        _historyRecordId = historyRecordId;
        _enableSelectableTextOnLoad = applyTextSelectableDefault && !startEditing && _settings.PinTextSelectableByDefault;
        _longScrollable = source.PixelHeight > source.PixelWidth * 2.5;
        _zoomReferenceWidth = Math.Max(1, source.Width > 0 ? source.Width : source.PixelWidth);
        PinnedImage.Source = source;
        LongPinnedImage.Source = source;
        Opacity = Math.Clamp(_settings.PinDefaultOpacity / 100.0, 0.15, 1);
        _shadowEnabled = _settings.PinWindowShadow;
        ApplyShadow();
        ApplyBackground();
        SizeToImage(source);
        _targetZoomBounds = new Rect(Left, Top, Width, Height);
        _zoomAnimationTimer.Tick += ZoomAnimation_Tick;
        _zoomBadgeTimer.Tick += (_, _) => { _zoomBadgeTimer.Stop(); ZoomBadge.Visibility = Visibility.Collapsed; };
        _refreshTimer.Tick += (_, _) => RefreshScreenshot(silent: true);
        _textAutoScrollTimer.Tick += TextAutoScrollTimer_Tick;
        _longScrollAnimationTimer.Tick += LongScrollAnimationTimer_Tick;
        ShowImageSurface();
        SizeChanged += (_, _) =>
        {
            UpdateLongImageWidth();
            if (_textSelectable) Dispatcher.BeginInvoke(UpdateSelectableTextLayout, DispatcherPriority.Loaded);
        };
        OpenPins.Add(this);
        SourceInitialized += (_, _) =>
        {
            ApplyCaptureAffinityToWindow();
            ApplyVirtualDesktopBinding();
        };
        Closed += (_, _) =>
        {
            _zoomAnimationTimer.Stop();
            _zoomBadgeTimer.Stop();
            _refreshTimer.Stop();
            _textAutoScrollTimer.Stop();
            _longScrollAnimationTimer.Stop();
            _selectableTextCancellation?.Cancel();
            _selectableTextCancellation?.Dispose();
            OpenPins.Remove(this);
            SelectedPins.Remove(this);
            NotifyPinsChanged();
        };
        InlineEditor.Applied += ApplyEditedImage;
        InlineEditor.DocumentStored += PersistEditedDocument;
        InlineEditor.Cancelled += (_, _) => ExitInlineMode();
        InlineCrop.Applied += ApplyCroppedImage;
        InlineCrop.Cancelled += (_, _) => ExitInlineMode();
        InlineRecognition.RecognitionCompleted += Recognition_Completed;
        InlineRecognition.Cancelled += (_, _) => ExitInlineMode();
        Loaded += async (_, _) =>
        {
            Focus();
            if (!_groupName.Equals(_settings.CurrentPinGroup, StringComparison.OrdinalIgnoreCase)) Hide();
            if (_startEditing) BeginEditMode();
            else if (_textSelectable && _recognizedWords.Count > 0) RebuildSelectableTextLayers();
            else if (_enableSelectableTextOnLoad) await SetTextSelectableAsync(true);
            UpdatePinStateBadge();
            NotifyPinsChanged();
        };
    }

    internal static IReadOnlyList<PinSnapshot> Snapshots() => OpenPins
        .OrderBy(pin => pin._groupName, StringComparer.CurrentCultureIgnoreCase)
        .ThenBy(pin => pin._pinId)
        .Select(pin => new PinSnapshot(
            pin._pinId,
            pin._source,
            pin._groupName,
            pin.IsVisible,
            VirtualDesktopService.IsOnCurrentDesktop(new WindowInteropHelper(pin).Handle),
            $"{Math.Round(pin.Width)} × {Math.Round(pin._inlineMode == "None" ? pin.Height : pin._normalHeight)}"))
        .ToList();

    public static void ActivatePin(Guid id)
    {
        var pin = OpenPins.FirstOrDefault(candidate => candidate._pinId == id);
        if (pin is null) return;
        SwitchGroup(pin._groupName);
        pin.Show();
        pin.WindowState = WindowState.Normal;
        pin.Activate();
        NotifyPinsChanged();
    }

    public static void ClosePin(Guid id) => OpenPins.FirstOrDefault(pin => pin._pinId == id)?.Close();

    public static void MovePin(Guid id, string group)
    {
        var pin = OpenPins.FirstOrDefault(candidate => candidate._pinId == id);
        if (pin is null) return;
        pin._groupName = group;
        pin.ApplyVirtualDesktopBinding();
        if (!group.Equals(SettingsService.Load().CurrentPinGroup, StringComparison.OrdinalIgnoreCase)) pin.Hide();
        SaveSession();
        NotifyPinsChanged();
    }

    public static void ReassignGroup(string oldGroup, string replacementGroup)
    {
        foreach (var pin in OpenPins.Where(pin => pin._groupName.Equals(oldGroup, StringComparison.OrdinalIgnoreCase)))
        {
            pin._groupName = replacementGroup;
            pin.ApplyVirtualDesktopBinding();
        }
        SwitchGroup(replacementGroup);
        SaveSession();
        NotifyPinsChanged();
    }

    public static void ApplyCaptureAffinity(bool exclude)
    {
        foreach (var pin in OpenPins.ToArray()) pin.ApplyCaptureAffinityToWindow(exclude);
    }

    public static void RestoreInteractions()
    {
        foreach (var pin in OpenPins.ToArray()) pin.SetClickThrough(false);
    }

    public static void ToggleAllVisibility()
    {
        var groupPins = OpenPins.Where(pin => pin._groupName.Equals(SettingsService.Load().CurrentPinGroup, StringComparison.OrdinalIgnoreCase)).ToList();
        var shouldHide = groupPins.Any(pin => pin.IsVisible);
        foreach (var pin in groupPins) { if (shouldHide) pin.Hide(); else pin.Show(); }
        NotifyPinsChanged();
    }

    public static void CloseAll()
    {
        foreach (var pin in OpenPins.ToArray()) pin.Close();
    }

    public static void CloseGroup(string groupName)
    {
        foreach (var pin in OpenPins.Where(pin => pin._groupName.Equals(groupName, StringComparison.OrdinalIgnoreCase)).ToArray())
            pin.Close();
    }

    public static void SwitchGroup(string groupName)
    {
        var settings = SettingsService.Load();
        if (!settings.PinGroups.Contains(groupName, StringComparer.OrdinalIgnoreCase)) return;
        settings.CurrentPinGroup = groupName;
        SettingsService.Save(settings);
        var targetDesktop = settings.PinGroupsFollowVirtualDesktops
            ? VirtualDesktopService.BoundDesktop(settings, groupName)
            : VirtualDesktopService.CurrentDesktopId();
        _soloMode = false;
        ClearSelection();
        foreach (var pin in OpenPins.ToArray())
        {
            if (pin._groupName.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            {
                if (targetDesktop is { } desktopId) pin.MoveToDesktop(desktopId);
                pin.Show();
            }
            else pin.Hide();
        }
        NotifyPinsChanged();
    }

    public static void ToggleSolo()
    {
        if (SelectedPins.Count == 0) return;
        _soloMode = !_soloMode;
        var current = SettingsService.Load().CurrentPinGroup;
        foreach (var pin in OpenPins.ToArray())
        {
            var visible = pin._groupName.Equals(current, StringComparison.OrdinalIgnoreCase) && (!_soloMode || SelectedPins.Contains(pin));
            if (visible) pin.Show(); else pin.Hide();
        }
        NotifyPinsChanged();
    }

    public static void SaveSession() => _ = SaveSessionAsync();

    public static Task SaveSessionAsync()
    {
        var settings = SettingsService.Load();
        if (!settings.AutoBackup)
            return PinSessionService.QueueClear();

        var snapshots = OpenPins.OrderBy(pin => pin._pinId).Select(pin =>
        {
            var image = pin._source.IsFrozen ? pin._source : pin._source.Clone();
            if (!image.IsFrozen) image.Freeze();
            var handle = new WindowInteropHelper(pin).Handle;
            var physicalRect = default(NativeMethods.NativeRect);
            var hasPhysicalBounds = handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out physicalRect);
            var screen = handle != IntPtr.Zero ? Forms.Screen.FromHandle(handle) : Forms.Screen.PrimaryScreen;
            var monitorBounds = screen?.Bounds ?? System.Drawing.Rectangle.Empty;
            return new PinSessionSnapshot(image, new PinSessionItem
            {
                Left = pin.Left,
                Top = pin.Top,
                Width = pin.Width,
                Height = pin._shaded ? Math.Max(60, pin._preShadeHeight) : pin._inlineMode == "None" ? pin.Height : pin._normalHeight,
                Opacity = pin.Opacity,
                Topmost = pin.Topmost,
                Group = pin._groupName,
                Background = pin._backgroundMode,
                DesktopId = VirtualDesktopService.GetDesktopId(new WindowInteropHelper(pin).Handle)?.ToString() ?? string.Empty,
                HistoryRecordId = pin._historyRecordId ?? string.Empty,
                TextSelectable = pin._textSelectable,
                OcrText = pin._selectableTextResult?.Text ?? string.Empty,
                OcrBarcodeText = pin._selectableTextResult?.BarcodeText ?? string.Empty,
                OcrBarcodeFormat = pin._selectableTextResult?.BarcodeFormat ?? string.Empty,
                OcrWords = pin._recognizedWords.ToList(),
                Title = pin._pinTitle,
                PositionLocked = pin._positionLocked,
                WindowShaded = pin._shaded,
                EdgeSnapping = pin._edgeSnapping,
                MonitorDeviceName = screen?.DeviceName ?? string.Empty,
                PhysicalLeft = hasPhysicalBounds ? physicalRect.Left : 0,
                PhysicalTop = hasPhysicalBounds ? physicalRect.Top : 0,
                PhysicalWidth = hasPhysicalBounds ? physicalRect.Width : 0,
                PhysicalHeight = hasPhysicalBounds ? physicalRect.Height : 0,
                MonitorLeft = monitorBounds.Left,
                MonitorTop = monitorBounds.Top,
                MonitorWidth = monitorBounds.Width,
                MonitorHeight = monitorBounds.Height,
                LongScrollOffset = pin._longScrollable ? pin.LongImageViewer.VerticalOffset : 0,
                IsVisible = pin.IsVisible,
                ClickThrough = pin._clickThrough
            });
        }).ToList();
        var fingerprint = string.Join("|", snapshots.Select(snapshot => FormattableString.Invariant(
            $"{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(snapshot.Image)}:{snapshot.Item.Left:R}:{snapshot.Item.Top:R}:{snapshot.Item.Width:R}:{snapshot.Item.Height:R}:{snapshot.Item.Opacity:R}:{snapshot.Item.Topmost}:{snapshot.Item.Group}:{snapshot.Item.Background}:{snapshot.Item.DesktopId}:{snapshot.Item.HistoryRecordId}:{snapshot.Item.TextSelectable}:{snapshot.Item.OcrWords.Count}:{snapshot.Item.Title}:{snapshot.Item.PositionLocked}:{snapshot.Item.WindowShaded}:{snapshot.Item.EdgeSnapping}:{snapshot.Item.MonitorDeviceName}:{snapshot.Item.PhysicalLeft}:{snapshot.Item.PhysicalTop}:{snapshot.Item.PhysicalWidth}:{snapshot.Item.PhysicalHeight}:{snapshot.Item.LongScrollOffset:R}:{snapshot.Item.IsVisible}:{snapshot.Item.ClickThrough}")));
        return PinSessionService.QueueSave(snapshots, fingerprint);
    }

    public static void RestoreSession()
    {
        if (OpenPins.Count > 0 || !SettingsService.Load().AutoBackup) return;
        foreach (var (image, item) in PinSessionService.Load())
        {
            var pin = new PinnedImageWindow(image, false, item.Group, item.Background, item.HistoryRecordId,
                applyTextSelectableDefault: false)
            {
                Opacity = item.Opacity,
                Topmost = item.Topmost
            };
            pin.RestoreSessionState(item);
            if (item.PhysicalWidth <= 0 || item.PhysicalHeight <= 0)
                pin.SetInitialBounds(new Rect(item.Left, item.Top, item.Width, item.Height));
            pin.Show();
            pin.RestorePhysicalPlacement(item);
            pin.Dispatcher.BeginInvoke(() =>
            {
                if (pin._longScrollable && item.LongScrollOffset > 0)
                    pin.LongImageViewer.ScrollToVerticalOffset(item.LongScrollOffset);
                if (item.ClickThrough) pin.SetClickThrough(true);
                if (!item.IsVisible) pin.Hide();
            }, DispatcherPriority.Loaded);
            var settings = SettingsService.Load();
            var desktop = settings.PinGroupsFollowVirtualDesktops
                ? VirtualDesktopService.BoundDesktop(settings, item.Group)
                : Guid.TryParse(item.DesktopId, out var restoredDesktop) ? restoredDesktop : null;
            if (desktop is { } desktopId) pin.MoveToDesktop(desktopId);
        }
    }

    private void RestorePhysicalPlacement(PinSessionItem item)
    {
        if (item.PhysicalWidth <= 0 || item.PhysicalHeight <= 0) return;
        var screens = Forms.Screen.AllScreens;
        var target = screens.FirstOrDefault(candidate =>
            candidate.DeviceName.Equals(item.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
        var desired = new System.Drawing.Rectangle(item.PhysicalLeft, item.PhysicalTop, item.PhysicalWidth, item.PhysicalHeight);
        if (target is null)
        {
            target = Forms.Screen.PrimaryScreen ?? screens.FirstOrDefault();
            if (target is null) return;
            var oldWidth = Math.Max(1, item.MonitorWidth);
            var oldHeight = Math.Max(1, item.MonitorHeight);
            var relativeX = (item.PhysicalLeft - item.MonitorLeft) / (double)oldWidth;
            var relativeY = (item.PhysicalTop - item.MonitorTop) / (double)oldHeight;
            desired.X = target.WorkingArea.Left + (int)Math.Round(relativeX * target.WorkingArea.Width);
            desired.Y = target.WorkingArea.Top + (int)Math.Round(relativeY * target.WorkingArea.Height);
        }
        SetScreenPixelBounds(ClampVisible(desired, target.WorkingArea));
    }

    private static System.Drawing.Rectangle ClampVisible(System.Drawing.Rectangle desired, System.Drawing.Rectangle workingArea)
    {
        var width = Math.Clamp(desired.Width, 80, Math.Max(80, workingArea.Width));
        var height = Math.Clamp(desired.Height, 60, Math.Max(60, workingArea.Height));
        var left = Math.Clamp(desired.Left, workingArea.Left - width + 48, workingArea.Right - 48);
        var top = Math.Clamp(desired.Top, workingArea.Top, workingArea.Bottom - 40);
        return new System.Drawing.Rectangle(left, top, width, height);
    }

    private void RestoreSelectableTextCache(PinSessionItem item)
    {
        if (item.OcrWords is not { Count: > 0 }) return;
        _recognizedTextSource = _source;
        _recognizedWords = item.OcrWords;
        _selectableTextResult = new RecognitionResult(
            item.OcrText,
            item.OcrBarcodeText,
            item.OcrBarcodeFormat,
            Words: _recognizedWords);
        _lastRecognitionFingerprint = $"{item.OcrText}\n{item.OcrBarcodeFormat}\n{item.OcrBarcodeText}";
        _textSelectable = item.TextSelectable;
    }

    private void RestoreSessionState(PinSessionItem item)
    {
        RestoreSelectableTextCache(item);
        _pinTitle = string.IsNullOrWhiteSpace(item.Title) ? "Pinned image" : item.Title.Trim();
        Title = $"SnapPin - {_pinTitle}";
        ShadeTitleText.Text = _pinTitle;
        _positionLocked = item.PositionLocked;
        _edgeSnapping = item.EdgeSnapping;
        if (item.WindowShaded) Dispatcher.BeginInvoke(() => SetShaded(true), DispatcherPriority.Loaded);
        UpdatePinStateBadge();
    }

    public void SetInitialBounds(Rect bounds)
    {
        StopZoomAnimation(false);
        Left = bounds.X; Top = bounds.Y;
        Width = Math.Max(80, bounds.Width); Height = Math.Max(60, bounds.Height);
        _normalHeight = Height;
        _targetZoomBounds = new Rect(Left, Top, Width, Height);
    }

    public void SetScreenPixelBounds(System.Drawing.Rectangle bounds)
    {
        StopZoomAnimation(false);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(
            handle,
            IntPtr.Zero,
            bounds.Left,
            bounds.Top,
            Math.Max(80, bounds.Width),
            Math.Max(60, bounds.Height),
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        Dispatcher.BeginInvoke(() =>
        {
            _normalHeight = Height;
            _targetZoomBounds = new Rect(Left, Top, Width, Height);
            UpdateLongImageWidth();
        });
    }

    public void BeginEditMode()
    {
        if (_inlineMode != "None") return;
        BeginInlineMode("Edit", 92);
        InlineEditor.Visibility = Visibility.Visible;
        if (!string.IsNullOrWhiteSpace(_historyRecordId) && HistoryService.Find(_historyRecordId) is { } record && record.HasEditableAnnotations)
            InlineEditor.LoadImage(HistoryService.LoadBaseImage(record), HistoryService.LoadAnnotations(record));
        else
            InlineEditor.LoadImage(_source);
        InlineEditor.Focus();
    }

    public void BeginCropMode()
    {
        if (_inlineMode != "None") return;
        BeginInlineMode("Crop", 56);
        InlineCrop.Visibility = Visibility.Visible;
        InlineCrop.LoadImage(_source);
        InlineCrop.Focus();
    }

    public void BeginRecognitionMode()
    {
        if (_inlineMode != "None") return;
        BeginInlineMode("Recognize", 176);
        InlineRecognition.Visibility = Visibility.Visible;
        InlineRecognition.LoadImage(_source);
        InlineRecognition.Focus();
    }

    private void BeginInlineMode(string mode, double toolbarHeight)
    {
        SetClickThrough(false);
        HideSelectableText();
        _inlineMode = mode;
        _normalHeight = Height;
        Height = Math.Min(SystemParameters.VirtualScreenHeight, Height + toolbarHeight);
        PinnedImage.Visibility = Visibility.Collapsed;
        LongImageViewer.Visibility = Visibility.Collapsed;
        ClickThroughBadge.Visibility = Visibility.Collapsed;
    }

    private void ApplyEditedImage(SnapPin.Controls.AnnotationAppliedEventArgs document)
    {
        SetBaseImage(document.FlattenedImage);
        PersistEditedDocument(document);
        ExitInlineMode();
    }

    private void PersistEditedDocument(SnapPin.Controls.AnnotationAppliedEventArgs document)
    {
        if (string.IsNullOrWhiteSpace(_historyRecordId))
            _historyRecordId = HistoryService.Add(document.BaseImage, sourceKind: "Edited").Id;
        HistoryService.SaveAnnotationDocument(_historyRecordId, document.BaseImage, document.FlattenedImage, document.Items);
        HistoryService.UpdateSourceKind(_historyRecordId, "Edited");
    }

    private void ApplyCroppedImage(BitmapSource image)
    {
        var displayWidth = Width;
        _normalHeight = Math.Max(60, displayWidth * image.PixelHeight / Math.Max(1, image.PixelWidth));
        SetBaseImage(image);
        _historyRecordId = HistoryService.Add(image, sourceKind: "Edited").Id;
        ExitInlineMode();
    }

    private void Recognition_Completed(object? sender, SnapPin.Controls.RecognitionCompletedEventArgs e)
    {
        PersistRecognitionResult(e.Result);
    }

    private void PersistRecognitionResult(RecognitionResult result)
    {
        if (!result.HasContent) return;
        var fingerprint = $"{result.Text}\n{result.BarcodeFormat}\n{result.BarcodeText}";
        if (fingerprint == _lastRecognitionFingerprint) return;
        _lastRecognitionFingerprint = fingerprint;
        if (string.IsNullOrWhiteSpace(_historyRecordId))
            _historyRecordId = HistoryService.SaveRecognition(_source, result.Text, result.BarcodeText, result.BarcodeFormat).Id;
        else
            HistoryService.UpdateRecognition(_historyRecordId, result.Text, result.BarcodeText, result.BarcodeFormat);
    }

    private void SetBaseImage(BitmapSource image)
    {
        InvalidateSelectableText();
        _baseSource = image;
        _zoomReferenceWidth = Math.Max(1, image.Width > 0 ? image.Width : image.PixelWidth);
        _grayscale = _inverted = false;
        ApplyFilters();
    }

    private void ExitInlineMode()
    {
        if (_inlineMode == "None") return;
        if (_inlineMode == "Recognize") InlineRecognition.CancelCurrent();
        InlineEditor.Visibility = Visibility.Collapsed;
        InlineCrop.Visibility = Visibility.Collapsed;
        InlineRecognition.Visibility = Visibility.Collapsed;
        ShowImageSurface();
        Height = Math.Max(60, _normalHeight);
        _inlineMode = "None";
        Focus();
    }

    private void SizeToImage(BitmapSource source)
    {
        var maxWidth = Math.Min(SystemParameters.WorkArea.Width * 0.68, _settings.PinMaxWindowSize);
        var maxHeight = Math.Min(SystemParameters.WorkArea.Height * 0.68, _settings.PinMaxWindowSize);
        var width = source.Width > 0 ? source.Width : source.PixelWidth;
        var height = source.Height > 0 ? source.Height : source.PixelHeight;
        if (_longScrollable)
        {
            Width = Math.Clamp(width, 320, maxWidth);
            Height = Math.Clamp(SystemParameters.WorkArea.Height * 0.72, 280, Math.Min(820, _settings.PinMaxWindowSize));
            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
            _normalHeight = Height;
            UpdateLongImageWidth();
            return;
        }
        var scale = Math.Min(1, Math.Min(maxWidth / Math.Max(1, width), maxHeight / Math.Max(1, height)));
        Width = Math.Max(120, width * scale); Height = Math.Max(80, height * scale);
        Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
        Top = SystemParameters.WorkArea.Top + (SystemParameters.WorkArea.Height - Height) / 2;
        _normalHeight = Height;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_inlineMode != "None" || TitleEditor.Visibility == Visibility.Visible) return;
        StopZoomAnimation(true);
        Focus();
        if (e.ClickCount == 2) { Close(); return; }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            SetSelected(!SelectedPins.Contains(this));
            e.Handled = true;
            return;
        }

        if (!SelectedPins.Contains(this))
        {
            ClearSelection();
            SetSelected(true);
        }

        if (_positionLocked)
        {
            ShowPinStatus("Position locked");
            e.Handled = true;
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed) return;
        var targets = SelectedPins.Contains(this) ? SelectedPins.ToArray() : [this];
        var positions = targets.ToDictionary(pin => pin, pin => new Point(pin.Left, pin.Top));
        var start = new Point(Left, Top);
        DragMove();
        if (_edgeSnapping) SnapToCurrentMonitorEdges();
        var dx = Left - start.X; var dy = Top - start.Y;
        foreach (var pin in targets.Where(pin => pin != this))
        {
            pin.Left = positions[pin].X + dx;
            pin.Top = positions[pin].Y + dy;
        }
        SaveSession();
    }

    private void SnapToCurrentMonitorEdges()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !NativeMethods.GetWindowRect(handle, out var rect)) return;
        var working = Forms.Screen.FromHandle(handle).WorkingArea;
        const int distance = 14;
        var x = rect.Left;
        var y = rect.Top;
        if (Math.Abs(rect.Left - working.Left) <= distance) x = working.Left;
        if (Math.Abs(rect.Top - working.Top) <= distance) y = working.Top;
        if (Math.Abs(rect.Right - working.Right) <= distance) x = working.Right - rect.Width;
        if (Math.Abs(rect.Bottom - working.Bottom) <= distance) y = working.Bottom - rect.Height;
        if (x != rect.Left || y != rect.Top)
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, rect.Width, rect.Height,
                NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_inlineMode != "None") return;
        var targets = SelectedPins.Contains(this) && SelectedPins.Count > 1 ? SelectedPins.ToArray() : [this];
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            foreach (var pin in targets) pin.Opacity = Math.Clamp(pin.Opacity + (e.Delta > 0 ? 0.08 : -0.08), 0.15, 1);
            e.Handled = true;
            return;
        }
        var factor = Math.Pow(1.045, e.Delta / 120.0);
        foreach (var pin in targets) pin.QueueSmoothResize(factor);
        e.Handled = true;
    }

    private void QueueSmoothResize(double factor)
    {
        var basis = _zoomAnimating ? _targetZoomBounds : new Rect(Left, Top, Width, Height);
        QueueSmoothResizeTo(
            Math.Clamp(basis.Width * factor, 60, SystemParameters.VirtualScreenWidth * 1.5),
            Math.Clamp(basis.Height * factor, 40, SystemParameters.VirtualScreenHeight * 1.5),
            basis.X + basis.Width / 2,
            basis.Y + basis.Height / 2);
    }

    private void QueueSmoothResizeTo(double width, double height, double centerX, double centerY)
    {
        _targetZoomBounds = new Rect(centerX - width / 2, centerY - height / 2, width, height);
        if (!_longScrollable) PinnedImage.CacheMode = _zoomBitmapCache;
        RenderOptions.SetBitmapScalingMode(PinnedImage, BitmapScalingMode.LowQuality);
        RenderOptions.SetBitmapScalingMode(LongPinnedImage, BitmapScalingMode.LowQuality);
        _zoomAnimating = true;
        _zoomAnimationTimer.Start();
        ShowZoomPercent(width);
    }

    private void ZoomAnimation_Tick(object? sender, EventArgs e)
    {
        const double blend = 0.42;
        Left += (_targetZoomBounds.Left - Left) * blend;
        Top += (_targetZoomBounds.Top - Top) * blend;
        Width += (_targetZoomBounds.Width - Width) * blend;
        Height += (_targetZoomBounds.Height - Height) * blend;
        _normalHeight = Height;
        if (Math.Abs(Left - _targetZoomBounds.Left) < 0.25 &&
            Math.Abs(Top - _targetZoomBounds.Top) < 0.25 &&
            Math.Abs(Width - _targetZoomBounds.Width) < 0.25 &&
            Math.Abs(Height - _targetZoomBounds.Height) < 0.25)
            StopZoomAnimation(true);
    }

    private void StopZoomAnimation(bool commitTarget)
    {
        if (commitTarget && _zoomAnimating && !_targetZoomBounds.IsEmpty)
        {
            Left = _targetZoomBounds.Left;
            Top = _targetZoomBounds.Top;
            Width = _targetZoomBounds.Width;
            Height = _targetZoomBounds.Height;
            _normalHeight = Height;
        }
        _zoomAnimating = false;
        _zoomAnimationTimer.Stop();
        RenderOptions.SetBitmapScalingMode(PinnedImage, BitmapScalingMode.HighQuality);
        RenderOptions.SetBitmapScalingMode(LongPinnedImage, BitmapScalingMode.HighQuality);
        PinnedImage.CacheMode = null;
        _targetZoomBounds = new Rect(Left, Top, Width, Height);
    }

    private void ApplyShadow() => Frame.Effect = _shadowEnabled
        ? new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.32 }
        : null;

    private void ShowZoomPercent(double width)
    {
        ZoomText.Text = $"{Math.Max(1, Math.Round(width / Math.Max(1, _zoomReferenceWidth) * 100))}%";
        ZoomBadge.Visibility = Visibility.Visible;
        _zoomBadgeTimer.Stop();
        _zoomBadgeTimer.Start();
    }

    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_inlineMode != "None") return;
        if (!SelectedPins.Contains(this)) { ClearSelection(); SetSelected(true); }
        var menu = new ContextMenu();

        var copySelectedText = Item("Copy selected text", (_, _) => CopySelectedText());
        copySelectedText.IsEnabled = !string.IsNullOrWhiteSpace(SelectedRecognizedText());
        menu.Items.Add(copySelectedText);
        menu.Items.Add(CheckItem("Text selectable (OCR)", _textSelectable, async (_, _) => await SetTextSelectableAsync(!_textSelectable)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Copy image", (_, _) => Clipboard.SetImage(_source)));
        menu.Items.Add(Item("Copy unscaled image", (_, _) => Clipboard.SetImage(_baseSource)));
        menu.Items.Add(Item("Save image as…", (_, _) => SaveBitmap(_source, applyOutputEffects: true)));
        menu.Items.Add(Item("Save unscaled image as…", (_, _) => SaveBitmap(_baseSource, applyOutputEffects: false)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Show toolbar", (_, _) => BeginEditMode()));
        menu.Items.Add(Item("Set title...    F2", (_, _) => BeginTitleEdit()));
        menu.Items.Add(CheckItem("Lock position    L", _positionLocked, (_, _) =>
        {
            _positionLocked = !_positionLocked;
            UpdatePinStateBadge();
            SaveSession();
        }));
        menu.Items.Add(CheckItem("Window shade    Y", _shaded, (_, _) => SetShaded(!_shaded)));
        menu.Items.Add(CheckItem("Snap to screen edges", _edgeSnapping, (_, _) =>
        {
            _edgeSnapping = !_edgeSnapping;
            UpdatePinStateBadge();
            SaveSession();
        }));
        menu.Items.Add(CheckItem("Shadow", _shadowEnabled, (_, _) => { _shadowEnabled = !_shadowEnabled; ApplyShadow(); }));

        var zoom = new MenuItem { Header = L("Zoom") };
        foreach (var percent in new[] { 25, 33, 50, 67, 75, 100, 125, 150, 200 })
            zoom.Items.Add(Item($"{percent}%", (_, _) => SetZoom(percent / 100.0)));
        menu.Items.Add(zoom);

        var processing = new MenuItem { Header = L("Image processing") };
        processing.Items.Add(Item("Rotate left", (_, _) => { _rotation -= 90; ApplyTransform(); }));
        processing.Items.Add(Item("Rotate right", (_, _) => { _rotation += 90; ApplyTransform(); }));
        processing.Items.Add(Item("Flip horizontally", (_, _) => { _flipX *= -1; ApplyTransform(); }));
        processing.Items.Add(Item("Flip vertically", (_, _) => { _flipY *= -1; ApplyTransform(); }));
        processing.Items.Add(new Separator());
        processing.Items.Add(CheckItem("Grayscale", _grayscale, (_, _) => { _grayscale = !_grayscale; ApplyFilters(); }));
        processing.Items.Add(CheckItem("Invert colors", _inverted, (_, _) => { _inverted = !_inverted; ApplyFilters(); }));
        processing.Items.Add(Item("Crop…", (_, _) => BeginCropMode()));
        menu.Items.Add(processing);

        menu.Items.Add(Item("Paste", (_, _) => ReplaceFromClipboard()));
        menu.Items.Add(Item("Replace by file…", (_, _) => ReplaceByFile()));

        var groups = new MenuItem { Header = L("Move to group") };
        foreach (var group in SettingsService.Load().PinGroups)
            groups.Items.Add(CheckItem(group, _groupName.Equals(group, StringComparison.OrdinalIgnoreCase), (_, _) => MoveTargetsToGroup(group)));
        menu.Items.Add(groups);
        if (!string.IsNullOrWhiteSpace(_sourceFilePath))
            menu.Items.Add(Item("View in folder", (_, _) => Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_sourceFilePath}\"") { UseShellExecute = true })));

        var tools = new MenuItem { Header = L("More tools") };
        tools.Items.Add(Item("Recognize text / QR…", (_, _) => BeginRecognitionMode()));
        tools.Items.Add(CheckItem("Fixed thumbnail", _thumbnail, (_, _) => ToggleThumbnail()));
        tools.Items.Add(CheckItem("Always on top", Topmost, (_, _) => { foreach (var pin in Targets()) pin.Topmost = !Topmost; }));
        tools.Items.Add(Item(_clickThrough ? "Make interactive" : "Enable click-through", (_, _) => SetClickThrough(!_clickThrough)));
        tools.Items.Add(CheckItem("Solo selected pins", _soloMode, (_, _) => ToggleSolo()));
        tools.Items.Add(Item("Copy image as file", (_, _) => CopyTargetsAsFiles()));
        if (Targets().Skip(1).Any()) tools.Items.Add(Item("Export selected images…", (_, _) => ExportSelectedImages()));
        tools.Items.Add(Item("Print…    Ctrl+P", (_, _) => PrintImage()));
        if (CanRefreshScreenshot)
        {
            tools.Items.Add(Item("Refresh screenshot    F5", (_, _) => RefreshScreenshot()));
            var automaticRefresh = new MenuItem { Header = L("Auto refresh") };
            automaticRefresh.Items.Add(CheckItem("Off", !_refreshTimer.IsEnabled, (_, _) => SetAutoRefresh(null)));
            foreach (var seconds in new[] { 1, 2, 5 })
                automaticRefresh.Items.Add(CheckItem(
                    LocalizationService.Format(seconds == 1 ? "Every {0} second" : "Every {0} seconds", seconds),
                    _refreshTimer.IsEnabled && Math.Abs(_refreshTimer.Interval.TotalSeconds - seconds) < 0.1,
                    (_, _) => SetAutoRefresh(TimeSpan.FromSeconds(seconds))));
            tools.Items.Add(automaticRefresh);
        }
        var backgrounds = new MenuItem { Header = L("Transparent background") };
        foreach (var mode in new[] { "Transparent", "Pseudo", "DarkChecker", "LightChecker" })
        {
            var label = mode switch { "Pseudo" => "Pseudo-transparent", "DarkChecker" => "Dark checkerboard", "LightChecker" => "Light checkerboard", _ => "Transparent" };
            backgrounds.Items.Add(CheckItem(label, _backgroundMode == mode, (_, _) => { _backgroundMode = mode; ApplyBackground(); }));
        }
        tools.Items.Add(backgrounds);
        tools.Items.Add(Item("Reset selected", (_, _) => { foreach (var pin in Targets()) pin.ResetAppearance(); }));
        menu.Items.Add(tools);

        menu.Items.Add(new Separator());
        menu.Items.Add(Item(SelectedPins.Count > 1 ? "Close selected" : "Close", (_, _) => { foreach (var pin in Targets().ToArray()) pin.Close(); }));
        menu.Items.Add(Item("Destroy", (_, _) => { foreach (var pin in Targets().ToArray()) pin.Close(); }));
        var dimensions = new MenuItem { Header = $"{Math.Round(Width)} × {Math.Round(_inlineMode == "None" ? Height : _normalHeight)}" };
        foreach (var percent in new[] { 50, 100, 150, 200 })
            dimensions.Items.Add(Item(LocalizationService.Format("Zoom to {0}%", percent), (_, _) => SetZoom(percent / 100.0)));
        menu.Items.Add(dimensions);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void BeginTitleEdit()
    {
        SetClickThrough(false);
        TitleEditorBox.Text = _pinTitle;
        TitleEditor.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            TitleEditorBox.Focus();
            TitleEditorBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void TitleEditorBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTitleEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            TitleEditor.Visibility = Visibility.Collapsed;
            Focus();
            e.Handled = true;
        }
    }

    private void TitleEditorBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (TitleEditor.Visibility == Visibility.Visible) CommitTitleEdit();
    }

    private void CommitTitleEdit()
    {
        _pinTitle = string.IsNullOrWhiteSpace(TitleEditorBox.Text) ? "Pinned image" : TitleEditorBox.Text.Trim();
        Title = $"SnapPin - {_pinTitle}";
        ShadeTitleText.Text = _pinTitle;
        TitleEditor.Visibility = Visibility.Collapsed;
        UpdatePinStateBadge();
        SaveSession();
        Focus();
    }

    private void SetShaded(bool shaded)
    {
        if (_shaded == shaded) return;
        if (shaded)
        {
            _preShadeHeight = Math.Max(60, Height);
            _shaded = true;
            StopZoomAnimation(false);
            Height = 30;
        }
        else
        {
            _shaded = false;
            Height = Math.Max(60, _preShadeHeight);
        }
        ShowImageSurface();
        UpdatePinStateBadge();
        SaveSession();
    }

    private void UpdatePinStateBadge()
    {
        ShadeTitleText.Text = _pinTitle;
        var states = new List<string>();
        if (_positionLocked) states.Add("Locked");
        if (_textSelectable) states.Add("OCR text");
        PinStateText.Text = string.Join(" | ", states);
        PinStateBadge.Visibility = !_shaded && states.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClickThroughBadge.Visibility = !_shaded && _clickThrough ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReplaceFromClipboard()
    {
        if (CaptureService.GetClipboardVisual() is not { } image) return;
        _sourceFilePath = string.Empty;
        SetBaseImage(image);
    }

    private void ReplaceByFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;
        SetBaseImage(CaptureService.LoadImage(dialog.FileName));
        _sourceFilePath = dialog.FileName;
    }

    private IEnumerable<PinnedImageWindow> Targets() => SelectedPins.Contains(this) ? SelectedPins : [this];

    private void CopyTargetsAsFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapPin", "ClipboardFiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var files = ExportTargets(directory, Targets().ToList(), forcePng: true);
        var dropList = new System.Collections.Specialized.StringCollection();
        dropList.AddRange(files.ToArray());
        Clipboard.SetFileDropList(dropList);
    }

    private void ExportSelectedImages()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            UseDescriptionForTitle = true,
            Description = "Choose a folder for the selected SnapPin images"
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        ExportTargets(dialog.SelectedPath, Targets().ToList(), forcePng: false);
        System.Media.SystemSounds.Asterisk.Play();
    }

    private static IReadOnlyList<string> ExportTargets(string directory, IReadOnlyList<PinnedImageWindow> pins, bool forcePng)
    {
        Directory.CreateDirectory(directory);
        var settings = SettingsService.Load();
        var extension = forcePng ? ".png" : CaptureService.ExtensionForFormat(settings.OutputFormat);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var paths = new List<string>(pins.Count);
        for (var index = 0; index < pins.Count; index++)
        {
            var path = Path.Combine(directory, $"SnapPin_{stamp}_{index + 1:D2}{extension}");
            CaptureService.SaveImage(pins[index]._source, path, settings.ImageQuality, settings);
            paths.Add(path);
        }
        return paths;
    }

    internal bool CanRefreshScreenshot => RefreshRecord() is not null;

    private CaptureRecord? RefreshRecord() =>
        string.IsNullOrWhiteSpace(_historyRecordId)
            ? null
            : HistoryService.Find(_historyRecordId) is { SourceRegion: not null } record ? record : null;

    private void SetAutoRefresh(TimeSpan? interval)
    {
        _refreshTimer.Stop();
        if (interval is null) return;
        _refreshTimer.Interval = interval.Value;
        _refreshTimer.Start();
        RefreshScreenshot();
    }

    private void RefreshScreenshot(bool silent = false)
    {
        try
        {
            if (RefreshRecord() is not { SourceRegion: { } region } record) return;
            var context = CaptureService.CaptureVirtualScreen();
            var freshBase = CaptureService.Crop(context, new Int32Rect(region.X, region.Y, region.Width, region.Height));
            var annotations = HistoryService.LoadAnnotations(record);
            BitmapSource refreshed = freshBase;
            if (annotations.Count > 0)
            {
                InlineEditor.LoadImage(freshBase, annotations);
                refreshed = InlineEditor.Flatten();
            }
            SetBaseImage(refreshed);
            HistoryService.SaveAnnotationDocument(record.Id, freshBase, refreshed, annotations);
            HistoryService.UpdateSourceKind(record.Id, "Refreshed");
            if (!silent) System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            _refreshTimer.Stop();
            if (!silent)
                MessageBox.Show(ex.Message, L("Refresh screenshot"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SetSelected(bool selected)
    {
        if (selected) SelectedPins.Add(this); else SelectedPins.Remove(this);
        Frame.BorderBrush = selected ? Brushes.DeepSkyBlue : new SolidColorBrush(Color.FromArgb(68, 0, 0, 0));
        Frame.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
    }

    private static void ClearSelection()
    {
        foreach (var pin in SelectedPins.ToArray()) pin.SetSelected(false);
    }

    private void MoveTargetsToGroup(string group)
    {
        foreach (var pin in Targets())
        {
            pin._groupName = group;
            pin.ApplyVirtualDesktopBinding();
        }
        if (!group.Equals(SettingsService.Load().CurrentPinGroup, StringComparison.OrdinalIgnoreCase))
            foreach (var pin in Targets().ToArray()) pin.Hide();
        ClearSelection();
        SaveSession();
        NotifyPinsChanged();
    }

    private void ApplyCaptureAffinityToWindow() => ApplyCaptureAffinityToWindow(SettingsService.Load().ExcludeSnapPinFromCapture);

    private void ApplyCaptureAffinityToWindow(bool exclude)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            NativeMethods.SetWindowDisplayAffinity(handle, exclude ? NativeMethods.WdaExcludeFromCapture : NativeMethods.WdaNone);
    }

    private void ApplyVirtualDesktopBinding()
    {
        var settings = SettingsService.Load();
        if (!settings.PinGroupsFollowVirtualDesktops) return;
        if (VirtualDesktopService.BoundDesktop(settings, _groupName) is { } desktopId) MoveToDesktop(desktopId);
    }

    private void MoveToDesktop(Guid desktopId)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) VirtualDesktopService.MoveWindowToDesktop(handle, desktopId);
    }

    private static void NotifyPinsChanged() => PinsChanged?.Invoke(null, EventArgs.Empty);

    private void ToggleThumbnail()
    {
        if (!_thumbnail)
        {
            _preThumbnailBounds = new Rect(Left, Top, Width, Height);
            Width = Height = _settings.FastThumbnailSize;
            LongImageViewer.Visibility = Visibility.Collapsed;
            PinnedImage.Visibility = Visibility.Visible;
            PinnedImage.Stretch = Stretch.UniformToFill;
            _thumbnail = true;
        }
        else
        {
            SetInitialBounds(_preThumbnailBounds);
            PinnedImage.Stretch = Stretch.Uniform;
            _thumbnail = false;
            ShowImageSurface();
        }
    }

    internal void SetThumbnailMode(bool enabled)
    {
        if (_thumbnail != enabled) ToggleThumbnail();
    }

    private void SetZoom(double scale)
    {
        var dpiWidth = _source.Width > 0 ? _source.Width : _source.PixelWidth;
        var dpiHeight = _source.Height > 0 ? _source.Height : _source.PixelHeight;
        var centerX = Left + Width / 2; var centerY = Top + Height / 2;
        QueueSmoothResizeTo(Math.Max(60, dpiWidth * scale), Math.Max(40, dpiHeight * scale), centerX, centerY);
    }

    private void ApplyFilters()
    {
        InvalidateSelectableText();
        _source = ImageTransformService.Apply(_baseSource, _grayscale, _inverted);
        PinnedImage.Source = _source;
        LongPinnedImage.Source = _source;
    }

    private void ApplyBackground()
    {
        Frame.Background = _backgroundMode switch
        {
            "DarkChecker" => Checkerboard(Color.FromRgb(55, 65, 81), Color.FromRgb(31, 41, 55)),
            "LightChecker" => Checkerboard(Color.FromRgb(255, 255, 255), Color.FromRgb(226, 232, 240)),
            _ => Brushes.Transparent
        };
    }

    private static Brush Checkerboard(Color first, Color second)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(first), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(second), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(second), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
    }

    private static MenuItem Item(string header, RoutedEventHandler action)
    {
        var item = new MenuItem { Header = L(header) };
        item.Click += action;
        return item;
    }

    private static string L(string value) => LocalizationService.Current(value);

    private static MenuItem CheckItem(string header, bool isChecked, RoutedEventHandler action)
    {
        var item = Item(header, action);
        item.IsCheckable = true; item.IsChecked = isChecked;
        return item;
    }

    private void SaveImage(object sender, RoutedEventArgs e)
        => SaveBitmap(_source, applyOutputEffects: true);

    private void SaveBitmap(BitmapSource source, bool applyOutputEffects)
    {
        var dialog = new SaveFileDialog
        {
            Filter = CaptureService.ImageSaveFilter,
            DefaultExt = CaptureService.ExtensionForFormat(_settings.OutputFormat),
            FilterIndex = CaptureService.FilterIndexForFormat(_settings.OutputFormat),
            AddExtension = true,
            FileName = $"SnapPin_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}{CaptureService.ExtensionForFormat(_settings.OutputFormat)}"
        };
        if (dialog.ShowDialog(this) == true)
            CaptureService.SaveImage(source, dialog.FileName, _settings.ImageQuality, applyOutputEffects ? _settings : null);
    }

    private void PrintImage()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var availableWidth = Math.Max(1, dialog.PrintableAreaWidth);
        var availableHeight = Math.Max(1, dialog.PrintableAreaHeight);
        var scale = Math.Min(availableWidth / Math.Max(1, _source.Width), availableHeight / Math.Max(1, _source.Height));
        var image = new Image
        {
            Source = _source,
            Width = Math.Max(1, _source.Width * Math.Min(1, scale)),
            Height = Math.Max(1, _source.Height * Math.Min(1, scale)),
            Stretch = Stretch.Uniform
        };
        image.Measure(new Size(image.Width, image.Height));
        image.Arrange(new Rect(0, 0, image.Width, image.Height));
        dialog.PrintVisual(image, "SnapPin image");
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (TitleEditor.Visibility == Visibility.Visible) return;
        if (_inlineMode != "None")
        {
            if (e.Key == Key.Escape) ExitInlineMode();
            return;
        }
        if (_textSelectable && e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
            !string.IsNullOrWhiteSpace(SelectedRecognizedText()))
        {
            CopySelectedText();
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.D1: _rotation -= 90; ApplyTransform(); break;
            case Key.D2: _rotation += 90; ApplyTransform(); break;
            case Key.D3: _flipX *= -1; ApplyTransform(); break;
            case Key.D4: _flipY *= -1; ApplyTransform(); break;
            case Key.D5: _grayscale = !_grayscale; ApplyFilters(); break;
            case Key.Space: Topmost = !Topmost; break;
            case Key.Delete: foreach (var pin in Targets().ToArray()) pin.Close(); break;
            case Key.E: BeginEditMode(); break;
            case Key.C: BeginCropMode(); break;
            case Key.R: BeginRecognitionMode(); break;
            case Key.F5: RefreshScreenshot(); break;
            case Key.F2: BeginTitleEdit(); break;
            case Key.L:
                _positionLocked = !_positionLocked;
                UpdatePinStateBadge();
                SaveSession();
                break;
            case Key.Y: SetShaded(!_shaded); break;
            case Key.P when (Keyboard.Modifiers & ModifierKeys.Control) != 0: PrintImage(); break;
        }
    }

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

    private async Task SetTextSelectableAsync(bool enabled)
    {
        if (!enabled)
        {
            HideSelectableText();
            SaveSession();
            return;
        }

        SetClickThrough(false);
        _textSelectable = true;
        ClearRecognizedTextSelection();
        ShowImageSurface();
        if (ReferenceEquals(_recognizedTextSource, _source) && _recognizedWords.Count > 0)
        {
            RebuildSelectableTextLayers();
            SaveSession();
            return;
        }

        _selectableTextCancellation?.Cancel();
        _selectableTextCancellation?.Dispose();
        var cancellation = _selectableTextCancellation = new CancellationTokenSource();
        var source = _source;
        ShowPinStatus("Recognizing text locally…");
        try
        {
            var result = await RecognitionService.RecognizeAsync(source, _settings.OcrLanguage, cancellation.Token);
            if (cancellation.IsCancellationRequested || !ReferenceEquals(source, _source)) return;
            _recognizedTextSource = source;
            _selectableTextResult = result;
            _recognizedWords = result.RecognizedWords;
            PersistRecognitionResult(result);
            if (_recognizedWords.Count == 0)
            {
                HideSelectableText();
                ShowPinStatus("No selectable text found");
                return;
            }
            RebuildSelectableTextLayers();
            SaveSession();
            ShowPinStatus($"{_recognizedWords.Count:N0} selectable words");
        }
        catch (OperationCanceledException)
        {
            if (_textSelectable) HideSelectableText();
        }
        catch (Exception ex)
        {
            HideSelectableText();
            ShowPinStatus($"Text recognition failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_selectableTextCancellation, cancellation))
            {
                _selectableTextCancellation.Dispose();
                _selectableTextCancellation = null;
            }
        }
    }

    private void ShowPinStatus(string text)
    {
        ZoomText.Text = text;
        ZoomBadge.Visibility = Visibility.Visible;
        _zoomBadgeTimer.Stop();
        _zoomBadgeTimer.Start();
    }

    private void HideSelectableText()
    {
        _textSelectable = false;
        _selectingText = false;
        StopTextAutoScroll();
        if (Mouse.Captured is Canvas captured && (captured == SelectableTextLayer || captured == LongSelectableTextLayer))
            captured.ReleaseMouseCapture();
        SelectableTextLayer.Visibility = Visibility.Collapsed;
        LongSelectableTextLayer.Visibility = Visibility.Collapsed;
        ClearRecognizedTextSelection();
        UpdatePinStateBadge();
    }

    private void InvalidateSelectableText()
    {
        _selectableTextCancellation?.Cancel();
        _selectableTextCancellation?.Dispose();
        _selectableTextCancellation = null;
        _recognizedTextSource = null;
        _selectableTextResult = null;
        _recognizedWords = Array.Empty<RecognizedWord>();
        SelectableTextLayer.Children.Clear();
        LongSelectableTextLayer.Children.Clear();
        _textHitRegions.Clear();
        HideSelectableText();
    }

    private void RebuildSelectableTextLayers()
    {
        RebuildSelectableTextLayer(SelectableTextLayer);
        RebuildSelectableTextLayer(LongSelectableTextLayer);
        ShowImageSurface();
        UpdatePinStateBadge();
        Dispatcher.BeginInvoke(UpdateSelectableTextLayout, DispatcherPriority.Loaded);
    }

    private void RebuildSelectableTextLayer(Canvas layer)
    {
        layer.Children.Clear();
        for (var index = 0; index < _recognizedWords.Count; index++)
        {
            layer.Children.Add(new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false,
                Tag = index,
                ToolTip = _recognizedWords[index].Text
            });
        }
        UpdateSelectableTextLayer(layer);
    }

    private void UpdateSelectableTextLayout()
    {
        UpdateSelectableTextLayer(SelectableTextLayer);
        UpdateSelectableTextLayer(LongSelectableTextLayer);
        PositionTextSelectionToolbar();
    }

    private void UpdateSelectableTextLayer(Canvas layer)
    {
        if (_recognizedWords.Count == 0 || layer.Children.Count != _recognizedWords.Count) return;
        var width = layer.ActualWidth > 1 ? layer.ActualWidth : layer.Width;
        var height = layer.ActualHeight > 1 ? layer.ActualHeight : layer.Height;
        if (double.IsNaN(width) || width <= 1 || double.IsNaN(height) || height <= 1) return;
        var scale = Math.Min(width / Math.Max(1.0, _source.PixelWidth), height / Math.Max(1.0, _source.PixelHeight));
        var offsetX = (width - _source.PixelWidth * scale) / 2;
        var offsetY = (height - _source.PixelHeight * scale) / 2;
        var regions = new List<Rect>(_recognizedWords.Count);
        for (var index = 0; index < _recognizedWords.Count; index++)
        {
            var word = _recognizedWords[index];
            var rect = new Rect(
                offsetX + word.X * scale,
                offsetY + word.Y * scale,
                Math.Max(3, word.Width * scale),
                Math.Max(3, word.Height * scale));
            regions.Add(rect);
            if (layer.Children[index] is not Border visual) continue;
            visual.Width = rect.Width;
            visual.Height = rect.Height;
            Canvas.SetLeft(visual, rect.X);
            Canvas.SetTop(visual, rect.Y);
        }
        _textHitRegions[layer] = regions;
        UpdateRecognizedTextSelectionVisuals();
    }

    private void SelectableText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_textSelectable || sender is not Canvas layer) return;
        var index = HitRecognizedWord(layer, e.GetPosition(layer), false);
        if (index is null) return;
        _textSelectionAnchor = _textSelectionEnd = index;
        if (e.ClickCount >= 2)
        {
            _selectingText = false;
            UpdateRecognizedTextSelectionVisuals();
            PositionTextSelectionToolbar(reanchor: _textSelectionToolbarPosition is null);
            e.Handled = true;
            return;
        }
        _selectingText = true;
        _textSelectionToolbarPosition = null;
        TextSelectionToolbar.Visibility = Visibility.Collapsed;
        layer.CaptureMouse();
        UpdateRecognizedTextSelectionVisuals();
        e.Handled = true;
    }

    private void SelectableText_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_selectingText || sender is not Canvas layer || e.LeftButton != MouseButtonState.Pressed) return;
        UpdateTextAutoScroll(layer);
        var index = HitRecognizedWord(layer, e.GetPosition(layer), true);
        if (index is null || index == _textSelectionEnd) return;
        _textSelectionEnd = index;
        UpdateRecognizedTextSelectionVisuals();
        e.Handled = true;
    }

    private void SelectableText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selectingText || sender is not Canvas layer) return;
        var index = HitRecognizedWord(layer, e.GetPosition(layer), true);
        if (index is not null) _textSelectionEnd = index;
        _selectingText = false;
        StopTextAutoScroll();
        if (Mouse.Captured == layer) layer.ReleaseMouseCapture();
        UpdateRecognizedTextSelectionVisuals();
        PositionTextSelectionToolbar(reanchor: true);
        e.Handled = true;
    }

    private void UpdateTextAutoScroll(Canvas layer)
    {
        if (layer != LongSelectableTextLayer || LongImageViewer.Visibility != Visibility.Visible)
        {
            StopTextAutoScroll();
            return;
        }
        var point = Mouse.GetPosition(LongImageViewer);
        _textAutoScrollDirection = point.Y < 30 ? -1 : point.Y > LongImageViewer.ViewportHeight - 30 ? 1 : 0;
        if (_textAutoScrollDirection == 0) _textAutoScrollTimer.Stop();
        else if (!_textAutoScrollTimer.IsEnabled) _textAutoScrollTimer.Start();
    }

    private void TextAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (!_selectingText || _textAutoScrollDirection == 0 || LongImageViewer.Visibility != Visibility.Visible)
        {
            StopTextAutoScroll();
            return;
        }
        LongImageViewer.ScrollToVerticalOffset(LongImageViewer.VerticalOffset + _textAutoScrollDirection * 18);
        var index = HitRecognizedWord(LongSelectableTextLayer, Mouse.GetPosition(LongSelectableTextLayer), true);
        if (index is not null) _textSelectionEnd = index;
        UpdateRecognizedTextSelectionVisuals();
    }

    private void StopTextAutoScroll()
    {
        _textAutoScrollDirection = 0;
        _textAutoScrollTimer.Stop();
    }

    private int? HitRecognizedWord(Canvas layer, Point point, bool allowNearest)
    {
        if (!_textHitRegions.TryGetValue(layer, out var regions)) return null;
        for (var index = 0; index < regions.Count; index++)
            if (regions[index].Contains(point)) return index;
        if (!allowNearest || regions.Count == 0) return null;
        var nearest = regions
            .Select((rect, index) => new { index, distance = DistanceToRect(point, rect) })
            .OrderBy(item => item.distance)
            .First();
        return nearest.distance <= 32 ? nearest.index : null;
    }

    private static double DistanceToRect(Point point, Rect rect)
    {
        var dx = point.X < rect.Left ? rect.Left - point.X : point.X > rect.Right ? point.X - rect.Right : 0;
        var dy = point.Y < rect.Top ? rect.Top - point.Y : point.Y > rect.Bottom ? point.Y - rect.Bottom : 0;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void UpdateRecognizedTextSelectionVisuals()
    {
        var start = Math.Min(_textSelectionAnchor ?? -1, _textSelectionEnd ?? -1);
        var end = Math.Max(_textSelectionAnchor ?? -1, _textSelectionEnd ?? -1);
        foreach (var layer in new[] { SelectableTextLayer, LongSelectableTextLayer })
        foreach (var visual in layer.Children.OfType<Border>())
        {
            var selected = visual.Tag is int index && index >= start && index <= end;
            visual.Background = selected ? new SolidColorBrush(Color.FromArgb(92, 51, 136, 255)) : Brushes.Transparent;
            visual.BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(210, 51, 136, 255)) : Brushes.Transparent;
            visual.BorderThickness = selected ? new Thickness(0.8) : new Thickness(0);
        }
    }

    private void PositionTextSelectionToolbar(bool reanchor = false)
    {
        if (_textSelectionAnchor is null || _textSelectionEnd is null || !_textSelectable)
        {
            TextSelectionToolbar.Visibility = Visibility.Collapsed;
            return;
        }
        var layer = LongSelectableTextLayer.Visibility == Visibility.Visible ? LongSelectableTextLayer : SelectableTextLayer;
        if (!_textHitRegions.TryGetValue(layer, out var regions) || regions.Count == 0 || Frame.Child is not Grid root) return;
        var start = Math.Clamp(Math.Min(_textSelectionAnchor.Value, _textSelectionEnd.Value), 0, regions.Count - 1);
        var end = Math.Clamp(Math.Max(_textSelectionAnchor.Value, _textSelectionEnd.Value), 0, regions.Count - 1);
        var bounds = regions[start];
        for (var index = start + 1; index <= end; index++) bounds.Union(regions[index]);
        TextSelectionToolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarSize = TextSelectionToolbar.DesiredSize;
        var below = default(Point);
        var above = default(Point);
        if (reanchor || _textSelectionToolbarPosition is null)
        {
            below = layer.TranslatePoint(new Point(bounds.Left, bounds.Bottom + 6), root);
            above = layer.TranslatePoint(new Point(bounds.Left, bounds.Top - toolbarSize.Height - 6), root);
        }
        _textSelectionToolbarPosition = StableTextToolbarPosition(_textSelectionToolbarPosition, below, above,
            toolbarSize, new Size(root.ActualWidth, root.ActualHeight), reanchor);
        TextSelectionToolbar.Margin = new Thickness(_textSelectionToolbarPosition.Value.X,
            _textSelectionToolbarPosition.Value.Y, 0, 0);
        TextSelectionToolbar.Visibility = Visibility.Visible;
    }

    internal static Point StableTextToolbarPosition(Point? current, Point below, Point above, Size toolbarSize,
        Size viewport, bool reanchor)
    {
        var maximumLeft = Math.Max(4, viewport.Width - toolbarSize.Width - 4);
        var maximumTop = Math.Max(4, viewport.Height - toolbarSize.Height - 4);
        var position = current;
        if (reanchor || position is null)
        {
            var anchorLeft = Math.Clamp(below.X, 4, maximumLeft);
            var anchorTop = below.Y + toolbarSize.Height <= viewport.Height - 4 ? below.Y : Math.Max(4, above.Y);
            position = new Point(anchorLeft, anchorTop);
        }
        return new Point(Math.Clamp(position.Value.X, 4, maximumLeft), Math.Clamp(position.Value.Y, 4, maximumTop));
    }

    private void ClearRecognizedTextSelection()
    {
        StopTextAutoScroll();
        _textSelectionAnchor = _textSelectionEnd = null;
        _textSelectionToolbarPosition = null;
        TextSelectionToolbar.Visibility = Visibility.Collapsed;
        UpdateRecognizedTextSelectionVisuals();
    }

    private string SelectedRecognizedText() => _textSelectionAnchor is int start && _textSelectionEnd is int end
        ? ComposeRecognizedText(_recognizedWords, start, end)
        : string.Empty;

    internal static string ComposeRecognizedText(IReadOnlyList<RecognizedWord> words, int start, int end)
    {
        if (words.Count == 0) return string.Empty;
        var first = Math.Clamp(Math.Min(start, end), 0, words.Count - 1);
        var last = Math.Clamp(Math.Max(start, end), first, words.Count - 1);
        return string.Join(Environment.NewLine, words.Skip(first).Take(last - first + 1)
            .GroupBy(word => word.LineIndex)
            .OrderBy(line => line.Key)
            .Select(ComposeRecognizedLine));
    }

    private static string ComposeRecognizedLine(IEnumerable<RecognizedWord> source)
    {
        var line = source.OrderBy(word => word.X).ToList();
        if (line.Count == 0) return string.Empty;
        var characterWidths = line
            .Where(word => word.Text.Length > 0)
            .Select(word => word.Width / (double)Math.Max(1, word.Text.Length))
            .OrderBy(value => value)
            .ToList();
        var typicalCharacterWidth = characterWidths.Count == 0 ? 5 : characterWidths[characterWidths.Count / 2];
        typicalCharacterWidth = Math.Max(3, typicalCharacterWidth);
        var result = new System.Text.StringBuilder(line[0].Text);
        for (var index = 1; index < line.Count; index++)
        {
            var previous = line[index - 1];
            var gap = line[index].X - (previous.X + previous.Width);
            result.Append(gap >= typicalCharacterWidth * 3.5 ? '\t' : ' ');
            result.Append(line[index].Text);
        }
        return result.ToString();
    }

    private void CopySelectedText()
    {
        CopyRecognizedText(SelectedRecognizedText());
    }

    private void CopySelectedText_Click(object sender, RoutedEventArgs e) => CopySelectedText();

    private void CopyAllRecognizedText_Click(object sender, RoutedEventArgs e)
    {
        CopyRecognizedText(_selectableTextResult?.Text?.Trim());
    }

    private void CopyRecognizedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Clipboard.SetText(text);
        ClearRecognizedTextSelection();
        ShowPinStatus("Copied");
    }

    private void SetClickThrough(bool enabled)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        if (enabled) style |= NativeMethods.WsExTransparent | NativeMethods.WsExLayered; else style &= ~NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(style));
        _clickThrough = enabled;
        UpdatePinStateBadge();
    }
}
