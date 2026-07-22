using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace SnapAnchor.Services;

/// <summary>
/// Best-effort HDR display discovery through the documented DisplayConfig API.
/// GDI exposes an SDR representation of an HDR desktop; on some drivers that
/// representation is visibly washed out. The conservative tone adjustment
/// below is only applied when Windows reports Advanced Color as enabled.
/// </summary>
internal static class HdrColorService
{
    private const uint QueryOnlyActivePaths = 0x00000002;
    private const int ErrorSuccess = 0;
    private const int SourceDeviceName = 1;
    private const int AdvancedColorInfo = 9;
    private const int SdrWhiteLevel = 11;
    private static readonly object Sync = new();
    private static readonly Dictionary<string, (DateTime Time, HdrState State)> Cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct HdrState(bool Enabled, double SdrWhiteNits);

    internal static BitmapSource CorrectIfNeeded(BitmapSource source, Drawing.Rectangle screenBounds, AppSettings settings)
    {
        if (!settings.CorrectHdrColors) return source;
        var center = new Drawing.Point(screenBounds.Left + screenBounds.Width / 2, screenBounds.Top + screenBounds.Height / 2);
        var device = System.Windows.Forms.Screen.FromPoint(center).DeviceName;
        var state = StateForDevice(device);
        if (!state.Enabled) return source;

        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var paperWhite = Math.Clamp(state.SdrWhiteNits / 80.0, 1.0, 5.0);
        var strength = Math.Clamp((paperWhite - 1.0) / 3.0, 0.12, 0.62);
        var gamma = 1.0 + 0.24 * strength;
        var saturation = 1.0 + 0.10 * strength;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var b = Math.Pow(pixels[index] / 255.0, gamma);
            var g = Math.Pow(pixels[index + 1] / 255.0, gamma);
            var r = Math.Pow(pixels[index + 2] / 255.0, gamma);
            var luma = 0.0722 * b + 0.7152 * g + 0.2126 * r;
            b = luma + (b - luma) * saturation;
            g = luma + (g - luma) * saturation;
            r = luma + (r - luma) * saturation;
            pixels[index] = ToByte(b);
            pixels[index + 1] = ToByte(g);
            pixels[index + 2] = ToByte(r);
        }

        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);

    private static HdrState StateForDevice(string device)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(device, out var cached) && DateTime.UtcNow - cached.Time < TimeSpan.FromSeconds(10))
                return cached.State;
        }
        var state = QueryState(device);
        lock (Sync) Cache[device] = (DateTime.UtcNow, state);
        return state;
    }

    private static HdrState QueryState(string device)
    {
        try
        {
            if (GetDisplayConfigBufferSizes(QueryOnlyActivePaths, out var pathCount, out var modeCount) != ErrorSuccess || pathCount == 0)
                return default;
            var paths = new DisplayConfigPathInfo[pathCount];
            var modeBytes = checked((int)Math.Max(1, modeCount) * 128);
            var modes = Marshal.AllocHGlobal(modeBytes);
            try
            {
                if (QueryDisplayConfig(QueryOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != ErrorSuccess)
                    return default;
                foreach (var path in paths.Take((int)pathCount))
                {
                    var source = new DisplayConfigSourceDeviceName
                    {
                        Header = Header(SourceDeviceName, Marshal.SizeOf<DisplayConfigSourceDeviceName>(), path.SourceInfo.AdapterId, path.SourceInfo.Id)
                    };
                    if (DisplayConfigGetDeviceInfo(ref source) != ErrorSuccess ||
                        !source.ViewGdiDeviceName.Equals(device, StringComparison.OrdinalIgnoreCase)) continue;

                    var advanced = new DisplayConfigGetAdvancedColorInfo
                    {
                        Header = Header(AdvancedColorInfo, Marshal.SizeOf<DisplayConfigGetAdvancedColorInfo>(), path.TargetInfo.AdapterId, path.TargetInfo.Id)
                    };
                    if (DisplayConfigGetDeviceInfo(ref advanced) != ErrorSuccess) return default;
                    var enabled = (advanced.Value & 0x2) != 0;
                    if (!enabled) return default;

                    var white = new DisplayConfigSdrWhiteLevel
                    {
                        Header = Header(SdrWhiteLevel, Marshal.SizeOf<DisplayConfigSdrWhiteLevel>(), path.TargetInfo.AdapterId, path.TargetInfo.Id)
                    };
                    var nits = DisplayConfigGetDeviceInfo(ref white) == ErrorSuccess && white.SdrWhiteLevel > 0
                        ? 80.0 * white.SdrWhiteLevel / 1000.0
                        : 160.0;
                    return new HdrState(true, nits);
                }
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
        catch
        {
            // Older systems/drivers can reject these queries. SDR capture is
            // the safe fallback and remains completely unchanged.
        }
        return default;
    }

    private static DisplayConfigDeviceInfoHeader Header(int type, int size, long adapterId, uint id)
        => new() { Type = type, Size = size, AdapterId = adapterId, Id = id };

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo { public long AdapterId; public uint Id; public uint ModeInfoIdx; public uint StatusFlags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational { public uint Numerator; public uint Denominator; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public long AdapterId; public uint Id; public uint ModeInfoIdx; public int OutputTechnology;
        public int Rotation; public int Scaling; public DisplayConfigRational RefreshRate; public int ScanLineOrdering;
        [MarshalAs(UnmanagedType.Bool)] public bool TargetAvailable;
        public uint StatusFlags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo { public DisplayConfigPathSourceInfo SourceInfo; public DisplayConfigPathTargetInfo TargetInfo; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader { public int Type; public int Size; public long AdapterId; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigGetAdvancedColorInfo
    {
        public DisplayConfigDeviceInfoHeader Header; public uint Value; public int ColorEncoding; public uint BitsPerColorChannel;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigSdrWhiteLevel { public DisplayConfigDeviceInfoHeader Header; public uint SdrWhiteLevel; }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);
    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [Out] DisplayConfigPathInfo[] pathInfoArray, ref uint numModeInfoArrayElements, IntPtr modeInfoArray, IntPtr currentTopologyId);
    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);
    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigGetAdvancedColorInfo requestPacket);
    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSdrWhiteLevel requestPacket);
}
