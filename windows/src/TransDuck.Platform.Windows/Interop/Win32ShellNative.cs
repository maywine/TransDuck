using System.Runtime.InteropServices;

namespace TransDuck.Platform.Windows.Interop;

internal static partial class Win32ShellNative
{
    public const uint NimAdd = 0x00000000;
    public const uint NimDelete = 0x00000002;
    public const uint NimSetVersion = 0x00000004;
    public const uint NifMessage = 0x00000001;
    public const uint NifIcon = 0x00000002;
    public const uint NifTip = 0x00000004;
    public const uint NotifyIconVersion4 = 4;
    public const int WmApp = 0x8000;
    public const int WmLeftButtonUp = 0x0202;
    public const int WmContextMenu = 0x007B;

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string text);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
    public static partial IntPtr LoadIcon(IntPtr instance, IntPtr iconName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr windowHandle);

    public static IntPtr LoadCurrentProcessIcon(IntPtr iconName)
    {
        var module = GetModuleHandle(null);
        var icon = module != IntPtr.Zero ? LoadIcon(module, iconName) : IntPtr.Zero;
        return icon != IntPtr.Zero ? icon : LoadIcon(IntPtr.Zero, iconName);
    }
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NotifyIconData
{
    public uint CbSize;
    public IntPtr WindowHandle;
    public uint Identifier;
    public uint Flags;
    public uint CallbackMessage;
    public IntPtr IconHandle;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Tooltip;

    public uint State;
    public uint StateMask;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string BalloonText;

    public uint VersionOrTimeout;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
    public string BalloonTitle;

    public uint BalloonFlags;
    public Guid ItemGuid;
    public IntPtr BalloonIconHandle;
}
