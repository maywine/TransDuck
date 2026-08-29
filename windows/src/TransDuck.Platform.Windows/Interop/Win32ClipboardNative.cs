using System.Runtime.InteropServices;
using System.Text;

namespace TransDuck.Platform.Windows.Interop;

/// <summary>
/// Centralizes raw clipboard handles and their ownership-transfer Win32 calls.
/// </summary>
internal static partial class Win32ClipboardNative
{
    private const int OpenAttempts = 5;
    private const int OpenRetryMilliseconds = 40;

    public const uint CfText = 1;
    public const uint CfBitmap = 2;
    public const uint CfSylk = 4;
    public const uint CfDif = 5;
    public const uint CfTiff = 6;
    public const uint CfOemText = 7;
    public const uint CfDib = 8;
    public const uint CfPenData = 10;
    public const uint CfRiff = 11;
    public const uint CfWave = 12;
    public const uint CfUnicodeText = 13;
    public const uint CfEnhMetaFile = 14;
    public const uint CfHDrop = 15;
    public const uint CfLocale = 16;
    public const uint CfDibV5 = 17;

    public const uint GmemMoveable = 0x0002;
    public const uint ImageBitmap = 0;
    public const uint LrCreatedibSection = 0x2000;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(IntPtr owner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr SetClipboardData(uint format, IntPtr memoryHandle);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", EntryPoint = "GetClipboardFormatNameW", CharSet = CharSet.Unicode)]
    public static extern int GetClipboardFormatName(uint format, StringBuilder formatName, int maxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial IntPtr CopyImage(
        IntPtr handle,
        uint imageType,
        int desiredWidth,
        int desiredHeight,
        uint flags);

    [LibraryImport("user32.dll")]
    public static partial uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern UIntPtr GlobalSize(IntPtr memoryHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr memoryHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(IntPtr memoryHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr memoryHandle);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr objectHandle);

    public static bool TryOpenClipboard(out int error) => TryOpenClipboard(IntPtr.Zero, out error);

    public static bool TryOpenClipboard(IntPtr owner, out int error)
    {
        error = 0;
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(owner))
            {
                return true;
            }

            error = Marshal.GetLastWin32Error();
            if (attempt < OpenAttempts - 1)
            {
                Thread.Sleep(OpenRetryMilliseconds);
            }
        }

        return false;
    }

    public static bool TryEmptyClipboard(out int error)
    {
        if (!ClipboardOwnerWindow.TryCreate(out var owner, out error))
        {
            return false;
        }

        using var ownerWindow = owner!;
        if (!TryOpenClipboard(ownerWindow.Handle, out error))
        {
            return false;
        }

        try
        {
            if (EmptyClipboard())
            {
                error = 0;
                return true;
            }

            error = Marshal.GetLastWin32Error();
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static IReadOnlyList<string> GetFormatNames()
    {
        if (!TryOpenClipboard(out _))
        {
            return [];
        }

        try
        {
            var names = new List<string>();
            var current = 0u;
            while ((current = EnumClipboardFormats(current)) != 0)
            {
                names.Add(GetFormatName(current));
            }

            return names;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public static string GetFormatName(uint format) => format switch
    {
        CfText => "Text",
        CfBitmap => "Bitmap",
        CfSylk => "Sylk",
        CfDif => "Dif",
        CfTiff => "Tiff",
        CfOemText => "OEMText",
        CfDib => "Dib",
        CfPenData => "PenData",
        CfRiff => "Riff",
        CfWave => "WaveAudio",
        CfUnicodeText => "UnicodeText",
        CfEnhMetaFile => "EnhancedMetafile",
        CfHDrop => "FileDrop",
        CfLocale => "Locale",
        CfDibV5 => "DibV5",
        _ => GetRegisteredFormatName(format),
    };

    private static string GetRegisteredFormatName(uint format)
    {
        var buffer = new StringBuilder(256);
        var count = GetClipboardFormatName(format, buffer, buffer.Capacity);
        return count > 0 ? buffer.ToString() : $"ClipboardFormat:{format}";
    }
}

/// <summary>
/// Supplies an owner HWND for clipboard mutations without borrowing a foreground or desktop window.
/// </summary>
internal sealed class ClipboardOwnerWindow : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private IntPtr _handle;

    private ClipboardOwnerWindow(IntPtr handle)
    {
        _handle = handle;
    }

    public IntPtr Handle => _handle;

    public static bool TryCreate(out ClipboardOwnerWindow? owner, out int error)
    {
        var handle = CreateWindowEx(
            WsExToolWindow,
            "STATIC",
            "TransDuckClipboardOwner",
            WsPopup,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            owner = null;
            error = Marshal.GetLastWin32Error();
            return false;
        }

        owner = new ClipboardOwnerWindow(handle);
        error = 0;
        return true;
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr handle);
}
