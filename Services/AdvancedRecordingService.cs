using ScreenRecorderLib;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace SnapAnchor.Services;

internal sealed record AdvancedRecordingResult(string FilePath, BitmapSource Preview, TimeSpan Duration, int FrameCount);
internal sealed record RecordingDeviceOption(string DeviceName, string DisplayName);

internal sealed class AdvancedRecordingSession : IDisposable
{
    private readonly Recorder _recorder;
    private readonly string _path;
    private readonly BitmapSource _preview;
    private readonly Stopwatch _activeTime = new();
    private readonly TaskCompletionSource<AdvancedRecordingResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _paused;
    private bool _stopped;

    private AdvancedRecordingSession(Recorder recorder, string path, BitmapSource preview)
    {
        _recorder = recorder;
        _path = path;
        _preview = preview;
        _recorder.OnRecordingComplete += RecordingComplete;
        _recorder.OnRecordingFailed += RecordingFailed;
    }

    public Task<AdvancedRecordingResult> Completion => _completion.Task;
    public TimeSpan Elapsed => _activeTime.Elapsed;
    public int FrameCount => _recorder.CurrentFrameNumber;
    public bool IsPaused => _paused;

    public static AdvancedRecordingSession Start(Rect selectedScreenBounds, AppSettings settings, IntPtr overlayHandle, IntPtr targetWindowHandle = default)
    {
        var source = CreateSource(selectedScreenBounds, settings.RecordingCaptureMode, settings.RecordingIncludeCursor, targetWindowHandle, out var previewBounds);
        var fps = settings.RecordingQuality switch { "Compact" => 24, "High" => 60, _ => 30 };
        var bitrate = settings.RecordingQuality switch { "Compact" => 3_000_000, "High" => 10_000_000, _ => 6_000_000 };
        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = [source] },
            OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.Main },
                Framerate = fps,
                Bitrate = bitrate,
                IsFixedFramerate = true,
                IsHardwareEncodingEnabled = true,
                IsLowLatencyEnabled = false,
                IsThrottlingDisabled = false,
                IsMp4FastStartEnabled = true
            },
            AudioOptions = new AudioOptions
            {
                IsAudioEnabled = settings.RecordingSystemAudio || settings.RecordingMicrophone,
                IsOutputDeviceEnabled = settings.RecordingSystemAudio,
                IsInputDeviceEnabled = settings.RecordingMicrophone,
                AudioOutputDevice = ResolveAudioDevice(settings.RecordingOutputDevice, OutputDevices()),
                AudioInputDevice = ResolveAudioDevice(settings.RecordingInputDevice, InputDevices()),
                OutputVolume = 1,
                InputVolume = 1
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = settings.RecordingIncludeCursor,
                IsMouseClicksDetected = settings.RecordingHighlightClicks,
                MouseClickDetectionMode = MouseDetectionMode.Hook,
                MouseClickDetectionDuration = 220,
                MouseClickDetectionRadius = 26,
                MouseLeftClickDetectionColor = "#FFFF3B30",
                MouseRightClickDetectionColor = "#FF3388FF"
            },
            LogOptions = new LogOptions { IsLogEnabled = false }
        };

        var output = SettingsService.CreateOutputPath(settings.RecordingFolder, "SnapAnchor_$yyyy-MM-dd_HH-mm-ss.mp4", ".mp4");
        var preview = CapturePreview(previewBounds, settings.RecordingIncludeCursor);
        var recorder = Recorder.CreateRecorder(options);
        if (overlayHandle != IntPtr.Zero) Recorder.SetExcludeFromCapture(overlayHandle, true);
        var session = new AdvancedRecordingSession(recorder, output, preview);
        session._activeTime.Start();
        recorder.Record(output);
        return session;
    }

    public void Pause()
    {
        if (_paused || _stopped) return;
        _recorder.Pause();
        _activeTime.Stop();
        _paused = true;
    }

    public void Resume()
    {
        if (!_paused || _stopped) return;
        _recorder.Resume();
        _activeTime.Start();
        _paused = false;
    }

    public async Task<AdvancedRecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        if (!_stopped)
        {
            _stopped = true;
            _activeTime.Stop();
            _recorder.Stop();
        }
        return await _completion.Task.WaitAsync(cancellationToken);
    }

    public static IReadOnlyList<RecordingDeviceOption> InputDevices()
    {
        try { return [new(string.Empty, "System default"), .. Recorder.GetSystemAudioDevices(AudioDeviceSource.InputDevices).Select(device => new RecordingDeviceOption(device.DeviceName, device.FriendlyName))]; }
        catch { return [new(string.Empty, "System default")]; }
    }

    public static IReadOnlyList<RecordingDeviceOption> OutputDevices()
    {
        try { return [new(string.Empty, "System default"), .. Recorder.GetSystemAudioDevices(AudioDeviceSource.OutputDevices).Select(device => new RecordingDeviceOption(device.DeviceName, device.FriendlyName))]; }
        catch { return [new(string.Empty, "System default")]; }
    }

    internal static string? ResolveAudioDevice(string? configured, IReadOnlyList<RecordingDeviceOption> available)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        return available.Any(device => device.DeviceName.Equals(configured, StringComparison.OrdinalIgnoreCase))
            ? configured
            : null;
    }

    private static RecordingSourceBase CreateSource(Rect bounds, string mode, bool cursor, IntPtr targetWindowHandle, out Rect previewBounds)
    {
        var center = new System.Drawing.Point((int)Math.Round(bounds.X + bounds.Width / 2), (int)Math.Round(bounds.Y + bounds.Height / 2));
        if (mode.Equals("Window", StringComparison.OrdinalIgnoreCase))
        {
            var handle = targetWindowHandle != IntPtr.Zero ? targetWindowHandle : NativeMethods.WindowFromPoint(new NativeMethods.NativePoint { X = center.X, Y = center.Y });
            var root = handle == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(handle, NativeMethods.GaRoot);
            if (root != IntPtr.Zero)
            {
                previewBounds = NativeMethods.GetWindowRect(root, out var nativeRect)
                    ? new Rect(nativeRect.Left, nativeRect.Top, nativeRect.Width, nativeRect.Height)
                    : bounds;
                var windowSource = Recorder.GetWindows().FirstOrDefault(window => window.Handle == root)
                    ?? new WindowRecordingSource { Handle = root };
                windowSource.IsBorderRequired = false;
                windowSource.IsCursorCaptureEnabled = cursor;
                return windowSource;
            }
        }

        var display = Forms.Screen.FromPoint(center);
        previewBounds = mode.Equals("FullScreen", StringComparison.OrdinalIgnoreCase)
            ? new Rect(display.Bounds.X, display.Bounds.Y, display.Bounds.Width, display.Bounds.Height)
            : Rect.Intersect(bounds, new Rect(display.Bounds.X, display.Bounds.Y, display.Bounds.Width, display.Bounds.Height));
        var source = Recorder.GetDisplays().FirstOrDefault(candidate => candidate.DeviceName.Equals(display.DeviceName, StringComparison.OrdinalIgnoreCase))
            ?? new DisplayRecordingSource { DeviceName = display.DeviceName };
        source.RecorderApi = RecorderApi.WindowsGraphicsCapture;
        source.IsBorderRequired = false;
        source.IsCursorCaptureEnabled = cursor;
        if (!mode.Equals("FullScreen", StringComparison.OrdinalIgnoreCase))
        {
            var intersection = previewBounds;
            source.SourceRect = new ScreenRect(
                Math.Max(0, intersection.X - display.Bounds.X),
                Math.Max(0, intersection.Y - display.Bounds.Y),
                MakeEven(intersection.Width),
                MakeEven(intersection.Height));
        }
        return source;
    }

    private static double MakeEven(double value) => Math.Max(2, Math.Floor(value / 2) * 2);

    private static BitmapSource CapturePreview(Rect bounds, bool cursor)
    {
        try { return CaptureService.CaptureScreenRect(bounds, cursor); }
        catch
        {
            var width = Math.Clamp((int)Math.Round(bounds.Width), 2, 640);
            var height = Math.Clamp((int)Math.Round(bounds.Height), 2, 360);
            var pixels = new byte[width * height * 4];
            var fallback = BitmapSource.Create(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, width * 4);
            fallback.Freeze();
            return fallback;
        }
    }

    private void RecordingComplete(object? sender, RecordingCompleteEventArgs e)
        => _completion.TrySetResult(new AdvancedRecordingResult(e.FilePath ?? _path, _preview, _activeTime.Elapsed, Math.Max(1, _recorder.CurrentFrameNumber)));

    private void RecordingFailed(object? sender, RecordingFailedEventArgs e)
        => _completion.TrySetException(new InvalidOperationException(e.Error ?? "The MP4 recording failed."));

    public void Dispose()
    {
        _recorder.OnRecordingComplete -= RecordingComplete;
        _recorder.OnRecordingFailed -= RecordingFailed;
        _recorder.Dispose();
    }
}
