using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Clipboard;

/// <summary>
/// Owns raw clipboard payload handles until successful SetClipboardData transfers them to Windows.
/// </summary>
internal abstract class ClipboardEntry(uint format, string name) : IDisposable
{
    public uint Format { get; } = format;

    public string Name { get; } = name;

    public virtual void Dispose()
    {
    }
}

internal sealed class GlobalMemoryClipboardEntry(uint format, string name, byte[] data)
    : ClipboardEntry(format, name)
{
    public byte[] Data { get; } = data;
}

internal sealed class BitmapClipboardEntry(uint format, string name, IntPtr handle)
    : ClipboardEntry(format, name)
{
    private IntPtr _handle = handle;

    public IntPtr Handle => _handle;

    public void TransferOwnership() => _handle = IntPtr.Zero;

    public override void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            Win32ClipboardNative.DeleteObject(_handle);
            _handle = IntPtr.Zero;
        }
    }
}

internal sealed class AllocatedGlobalMemory(IntPtr handle) : IDisposable
{
    private IntPtr _handle = handle;

    public IntPtr Handle => _handle;

    public void TransferOwnership() => _handle = IntPtr.Zero;

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            Win32ClipboardNative.GlobalFree(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
