using System.Windows.Interop;

namespace TransDuck.Platform.Windows.Interop;

/// <summary>
/// Owns either a message-only HWND or a hidden top-level HWND for native adapters.
/// </summary>
public sealed class NativeMessageWindow : IDisposable
{
    private static readonly IntPtr MessageOnlyWindow = new(-3);
    private readonly HwndSource _source;
    private bool _disposed;

    public NativeMessageWindow(NativeWindowKind kind)
    {
        var parameters = new HwndSourceParameters("TransDuck.NativeMessageWindow")
        {
            Width = 0,
            Height = 0,
        };

        if (kind == NativeWindowKind.MessageOnly)
        {
            parameters.ParentWindow = MessageOnlyWindow;
            parameters.WindowStyle = 0;
        }
        else
        {
            // TaskbarCreated is broadcast to top-level windows, not HWND_MESSAGE windows.
            parameters.WindowStyle = unchecked((int)0x80000000); // WS_POPUP
            parameters.ExtendedWindowStyle = 0x00000080; // WS_EX_TOOLWINDOW
        }

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public IntPtr Handle => _source.Handle;

    public event EventHandler<NativeWindowMessageEventArgs>? MessageReceived;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        MessageReceived?.Invoke(this, new NativeWindowMessageEventArgs(hwnd, message, wParam, lParam));
        return IntPtr.Zero;
    }
}

public enum NativeWindowKind
{
    MessageOnly,
    HiddenTopLevel,
}

public sealed class NativeWindowMessageEventArgs(
    IntPtr handle,
    int message,
    IntPtr wParam,
    IntPtr lParam) : EventArgs
{
    public IntPtr Handle { get; } = handle;

    public int Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;
}
