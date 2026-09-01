using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

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
    public const int SmCxSmallIcon = 49;
    public const int SmCySmallIcon = 50;
    public const uint IconResourceVersion = 0x00030000;
    public const uint LrDefaultColor = 0x00000000;

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [LibraryImport("user32.dll", EntryPoint = "RegisterWindowMessageW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessage(string text);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern unsafe SafeIconHandle CreateIconFromResourceEx(
        byte* resourceBits,
        uint resourceSize,
        [MarshalAs(UnmanagedType.Bool)] bool isIcon,
        uint version,
        int desiredWidth,
        int desiredHeight,
        uint flags);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(IntPtr windowHandle);

    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr iconHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr windowHandle);

}

internal sealed class SafeIconHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeIconHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => Win32ShellNative.DestroyIcon(handle);
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
