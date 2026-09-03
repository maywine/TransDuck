using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TransDuck.Platform.Windows.Interop;

/// <summary>
/// Owns either a message-only HWND or a hidden top-level HWND without depending on WPF.
/// </summary>
public sealed class NativeMessageWindow : IDisposable
{
    private const uint PopupWindowStyle = 0x80000000;
    private const uint ToolWindowExtendedStyle = 0x00000080;
    private const int ErrorClassAlreadyExists = 1410;
    private static readonly IntPtr MessageOnlyWindow = new(-3);
    private static readonly string WindowClassName = "TransDuck.NativeMessageWindow";
    private static readonly object ClassRegistrationGate = new();
    private static readonly ConcurrentDictionary<IntPtr, NativeMessageWindow> Windows = new();
    private static readonly WindowProcedure SharedWindowProcedure = WndProc;
    private static bool _classRegistered;
    private IntPtr _handle;

    public NativeMessageWindow(NativeWindowKind kind)
    {
        EnsureWindowClass();
        var parent = kind == NativeWindowKind.MessageOnly ? MessageOnlyWindow : IntPtr.Zero;
        var style = kind == NativeWindowKind.MessageOnly ? 0u : PopupWindowStyle;
        var extendedStyle = kind == NativeWindowKind.MessageOnly ? 0u : ToolWindowExtendedStyle;
        _handle = CreateWindowEx(
            extendedStyle,
            WindowClassName,
            "TransDuck.NativeMessageWindow",
            style,
            0,
            0,
            0,
            0,
            parent,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);
        if (_handle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        if (!Windows.TryAdd(_handle, this))
        {
            DestroyWindow(_handle);
            _handle = IntPtr.Zero;
            throw new InvalidOperationException("The native message window handle is already registered.");
        }
    }

    public IntPtr Handle => _handle;

    public event EventHandler<NativeWindowMessageEventArgs>? MessageReceived;

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, IntPtr.Zero);
        if (handle == IntPtr.Zero)
        {
            return;
        }

        Windows.TryRemove(handle, out _);
        DestroyWindow(handle);
    }

    private static void EnsureWindowClass()
    {
        lock (ClassRegistrationGate)
        {
            if (_classRegistered)
            {
                return;
            }

            var windowClass = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                WindowProcedure = SharedWindowProcedure,
                Instance = GetModuleHandle(null),
                ClassName = WindowClassName,
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != ErrorClassAlreadyExists)
                {
                    throw new Win32Exception(error);
                }
            }

            _classRegistered = true;
        }
    }

    private static IntPtr WndProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (Windows.TryGetValue(handle, out var window))
        {
            try
            {
                window.MessageReceived?.Invoke(
                    window,
                    new NativeWindowMessageEventArgs(handle, unchecked((int)message), wParam, lParam));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // Managed exceptions must never cross the native window-procedure boundary.
            }
        }

        return DefWindowProc(handle, message, wParam, lParam);
    }

    [DllImport("user32.dll", EntryPoint = "RegisterClassExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass windowClass);

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

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr WindowProcedure(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        [MarshalAs(UnmanagedType.FunctionPtr)]
        public WindowProcedure WindowProcedure;
        public int ClassExtraBytes;
        public int WindowExtraBytes;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr BackgroundBrush;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
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
