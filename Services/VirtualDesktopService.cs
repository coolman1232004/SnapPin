using System.Runtime.InteropServices;

namespace SnapPin.Services;

internal static class VirtualDesktopService
{
    private static readonly Lazy<IVirtualDesktopManager?> Manager = new(CreateManager);

    public static bool IsSupported => Manager.Value is not null;

    public static Guid? CurrentDesktopId()
    {
        var handle = NativeMethods.GetForegroundWindow();
        return GetDesktopId(handle);
    }

    public static Guid? GetDesktopId(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || Manager.Value is not { } manager) return null;
        try { return manager.GetWindowDesktopId(windowHandle, out var desktopId) >= 0 ? desktopId : null; }
        catch { return null; }
    }

    public static bool IsOnCurrentDesktop(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero || Manager.Value is not { } manager) return true;
        try { return manager.IsWindowOnCurrentVirtualDesktop(windowHandle, out var current) >= 0 && current != 0; }
        catch { return true; }
    }

    public static bool MoveWindowToDesktop(IntPtr windowHandle, Guid desktopId)
    {
        if (windowHandle == IntPtr.Zero || desktopId == Guid.Empty || Manager.Value is not { } manager) return false;
        try { return manager.MoveWindowToDesktop(windowHandle, desktopId) >= 0; }
        catch { return false; }
    }

    public static Guid? BoundDesktop(AppSettings settings, string groupName)
        => settings.PinGroupDesktopBindings.TryGetValue(groupName, out var value) && Guid.TryParse(value, out var id) ? id : null;

    public static string ShortLabel(Guid? desktopId)
        => desktopId is { } id ? $"Desktop {id.ToString("N")[..4].ToUpperInvariant()}" : "Desktop unavailable";

    private static IVirtualDesktopManager? CreateManager()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), throwOnError: false);
            return type is null ? null : Activator.CreateInstance(type) as IVirtualDesktopManager;
        }
        catch { return null; }
    }

    [ComImport]
    [Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig]
        int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, out int onCurrentDesktop);

        [PreserveSig]
        int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

        [PreserveSig]
        int MoveWindowToDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.LPStruct)] Guid desktopId);
    }
}
