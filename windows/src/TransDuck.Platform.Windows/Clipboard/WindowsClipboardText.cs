using System.Runtime.InteropServices;
using System.Text;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Clipboard;

/// <summary>
/// Reads and writes plain text through the Win32 clipboard without a WPF dependency.
/// </summary>
public static class WindowsClipboardText
{
    public static bool TryRead(out string text, out int error)
    {
        text = string.Empty;
        if (!Win32ClipboardNative.TryOpenClipboard(out error))
        {
            return false;
        }

        try
        {
            var unicode = Win32ClipboardNative.GetClipboardData(Win32ClipboardNative.CfUnicodeText);
            if (unicode != IntPtr.Zero)
            {
                return TryReadLocked(unicode, unicodeText: true, out text, out error);
            }

            var ansi = Win32ClipboardNative.GetClipboardData(Win32ClipboardNative.CfText);
            if (ansi != IntPtr.Zero)
            {
                return TryReadLocked(ansi, unicodeText: false, out text, out error);
            }

            error = 0;
            return false;
        }
        finally
        {
            Win32ClipboardNative.CloseClipboard();
        }
    }

    public static bool TryWrite(string text, out int error)
    {
        ArgumentNullException.ThrowIfNull(text);
        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = Win32ClipboardNative.GlobalAlloc(
            Win32ClipboardNative.GmemMoveable,
            new UIntPtr((uint)bytes.Length));
        if (memory == IntPtr.Zero)
        {
            error = Marshal.GetLastWin32Error();
            return false;
        }

        var transferred = false;
        try
        {
            var destination = Win32ClipboardNative.GlobalLock(memory);
            if (destination == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                Win32ClipboardNative.GlobalUnlock(memory);
            }

            if (!ClipboardOwnerWindow.TryCreate(out var owner, out error))
            {
                return false;
            }

            using (var ownerWindow = owner!)
            {
                if (!Win32ClipboardNative.TryOpenClipboard(ownerWindow.Handle, out error))
                {
                    return false;
                }

                try
                {
                    if (!Win32ClipboardNative.EmptyClipboard())
                    {
                        error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    if (Win32ClipboardNative.SetClipboardData(
                            Win32ClipboardNative.CfUnicodeText,
                            memory) == IntPtr.Zero)
                    {
                        error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    transferred = true;
                    error = 0;
                    return true;
                }
                finally
                {
                    Win32ClipboardNative.CloseClipboard();
                }
            }
        }
        finally
        {
            if (!transferred)
            {
                Win32ClipboardNative.GlobalFree(memory);
            }
        }
    }

    private static bool TryReadLocked(
        IntPtr memory,
        bool unicodeText,
        out string text,
        out int error)
    {
        var pointer = Win32ClipboardNative.GlobalLock(memory);
        if (pointer == IntPtr.Zero)
        {
            text = string.Empty;
            error = Marshal.GetLastWin32Error();
            return false;
        }

        try
        {
            text = unicodeText
                ? Marshal.PtrToStringUni(pointer) ?? string.Empty
                : Marshal.PtrToStringAnsi(pointer) ?? string.Empty;
            error = 0;
            return true;
        }
        finally
        {
            Win32ClipboardNative.GlobalUnlock(memory);
        }
    }
}
