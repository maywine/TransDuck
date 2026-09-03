using System.ComponentModel;
using System.Runtime.InteropServices;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Tray;

/// <summary>
/// Shows the notification-area menu through Win32 so it remains independent of the UI framework.
/// </summary>
public sealed partial class ShellTrayContextMenu : IDisposable
{
    private const uint MenuString = 0x00000000;
    private const uint MenuSeparator = 0x00000800;
    private const uint TrackRightButton = 0x0002;
    private const uint TrackReturnCommand = 0x0100;
    private const uint TrackNoNotify = 0x0080;
    private const uint WmNull = 0x0000;
    private readonly NativeMessageWindow _owner;
    private readonly IReadOnlyList<ShellTrayMenuEntry> _entries;
    private bool _disposed;

    public ShellTrayContextMenu(
        NativeMessageWindow owner,
        IReadOnlyList<ShellTrayMenuEntry> entries)
    {
        _owner = owner;
        _entries = entries;
    }

    public Action? Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var actions = new Dictionary<uint, Action>();
            var command = 1u;
            foreach (var entry in _entries)
            {
                if (entry.IsSeparator)
                {
                    if (!AppendMenu(menu, MenuSeparator, UIntPtr.Zero, null))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    continue;
                }

                if (!AppendMenu(menu, MenuString, new UIntPtr(command), entry.Label))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                actions.Add(command, entry.Action!);
                command++;
            }

            if (!GetCursorPos(out var cursor))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Win32ShellNative.SetForegroundWindow(_owner.Handle);
            var selected = TrackPopupMenuEx(
                menu,
                TrackRightButton | TrackReturnCommand | TrackNoNotify,
                cursor.X,
                cursor.Y,
                _owner.Handle,
                IntPtr.Zero);
            PostMessage(_owner.Handle, WmNull, IntPtr.Zero, IntPtr.Zero);
            return selected != 0 && actions.TryGetValue(selected, out var action)
                ? action
                : null;
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose() => _disposed = true;

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenu(IntPtr menu, uint flags, UIntPtr item, string? label);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint TrackPopupMenuEx(
        IntPtr menu,
        uint flags,
        int x,
        int y,
        IntPtr owner,
        IntPtr parameters);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}

public sealed record ShellTrayMenuEntry(string? Label, Action? Action, bool IsSeparator)
{
    public static ShellTrayMenuEntry Command(string label, Action action) => new(label, action, false);

    public static ShellTrayMenuEntry Separator() => new(null, null, true);
}
