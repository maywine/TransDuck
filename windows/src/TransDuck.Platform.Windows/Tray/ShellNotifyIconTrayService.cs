using System.Runtime.InteropServices;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Tray;

/// <summary>
/// Keeps a Shell_NotifyIcon entry alive and re-adds it when Explorer recreates its taskbar.
/// </summary>
public sealed class ShellNotifyIconTrayService : IDisposable
{
    private const uint IconIdentifier = 1;
    private static readonly IntPtr ApplicationIconId = new(32512);
    private readonly NativeMessageWindow _messageWindow;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _callbackMessage;
    private bool _isAdded;
    private bool _disposed;

    public ShellNotifyIconTrayService(NativeMessageWindow messageWindow, string tooltip)
    {
        _messageWindow = messageWindow;
        Tooltip = tooltip;
        _callbackMessage = Win32ShellNative.WmApp + 37;
        _taskbarCreatedMessage = Win32ShellNative.RegisterWindowMessage("TaskbarCreated");
        _messageWindow.MessageReceived += HandleMessage;
    }

    public string Tooltip { get; }

    public event EventHandler? PrimaryActionRequested;

    public event EventHandler? ContextMenuRequested;

    public event EventHandler<TrayOperationResult>? ExplorerRestarted;

    public TrayOperationResult Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return AddIcon();
    }

    public TrayOperationResult Stop()
    {
        if (!_isAdded)
        {
            return TrayOperationResult.NotRunning();
        }

        var data = CreateData();
        if (!Win32ShellNative.ShellNotifyIcon(Win32ShellNative.NimDelete, ref data))
        {
            return TrayOperationResult.Failed("无法从通知区域移除 TransDuck 图标。",
                Marshal.GetLastWin32Error());
        }

        _isAdded = false;
        return TrayOperationResult.Removed();
    }

    /// <summary>
    /// Activates the hidden top-level owner before a caller opens the notification-area context menu.
    /// </summary>
    public bool TryActivateContextMenuOwner()
    {
        if (_disposed)
        {
            return false;
        }

        return Win32ShellNative.SetForegroundWindow(_messageWindow.Handle);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _messageWindow.MessageReceived -= HandleMessage;
        _disposed = true;
    }

    private TrayOperationResult AddIcon()
    {
        var data = CreateData();
        if (!Win32ShellNative.ShellNotifyIcon(Win32ShellNative.NimAdd, ref data))
        {
            _isAdded = false;
            return TrayOperationResult.Failed("无法将 TransDuck 添加到通知区域。",
                Marshal.GetLastWin32Error());
        }

        data.VersionOrTimeout = Win32ShellNative.NotifyIconVersion4;
        if (!Win32ShellNative.ShellNotifyIcon(Win32ShellNative.NimSetVersion, ref data))
        {
            _isAdded = true;
            return TrayOperationResult.Failed("通知区域图标已添加，但无法启用版本 4 回调。",
                Marshal.GetLastWin32Error());
        }

        _isAdded = true;
        return TrayOperationResult.Added();
    }

    private NotifyIconData CreateData() => new()
    {
        CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
        WindowHandle = _messageWindow.Handle,
        Identifier = IconIdentifier,
        Flags = Win32ShellNative.NifMessage | Win32ShellNative.NifIcon | Win32ShellNative.NifTip,
        CallbackMessage = _callbackMessage,
        IconHandle = Win32ShellNative.LoadCurrentProcessIcon(ApplicationIconId),
        Tooltip = Tooltip,
        BalloonText = string.Empty,
        BalloonTitle = string.Empty,
    };

    private void HandleMessage(object? sender, NativeWindowMessageEventArgs args)
    {
        if (args.Message == unchecked((int)_taskbarCreatedMessage))
        {
            _isAdded = false;
            var result = AddIcon();
            ExplorerRestarted?.Invoke(this, result);
            return;
        }

        if (args.Message != unchecked((int)_callbackMessage))
        {
            return;
        }

        var notification = unchecked((int)(args.LParam.ToInt64() & 0xFFFF));
        if (notification == Win32ShellNative.WmLeftButtonUp)
        {
            PrimaryActionRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (notification == Win32ShellNative.WmContextMenu)
        {
            ContextMenuRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed class TrayOperationResult : EventArgs
{
    private TrayOperationResult(bool succeeded, string status, int? win32Error = null)
    {
        Succeeded = succeeded;
        Status = status;
        Win32Error = win32Error;
    }

    public bool Succeeded { get; }

    public string Status { get; }

    public int? Win32Error { get; }

    public static TrayOperationResult Added() => new(true, "通知区域图标已添加。");

    public static TrayOperationResult Removed() => new(true, "通知区域图标已移除。");

    public static TrayOperationResult NotRunning() => new(true, "通知区域图标未运行。");

    public static TrayOperationResult Failed(string status, int error) => new(false, status, error);
}
