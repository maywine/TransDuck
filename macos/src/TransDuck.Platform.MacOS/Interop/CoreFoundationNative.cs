using System.Runtime.InteropServices;

namespace TransDuck.Platform.MacOS.Interop;

internal static partial class CoreFoundationNative
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8Encoding = 0x08000100;

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(
        IntPtr allocator,
        string value,
        uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDataCreate(
        IntPtr allocator,
        byte[] bytes,
        nint length);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDataGetBytePtr(IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFGetTypeID(IntPtr value);

    [LibraryImport(CoreFoundation)]
    private static partial nuint CFStringGetTypeID();

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetLength(IntPtr value);

    [LibraryImport(CoreFoundation)]
    private static partial nint CFStringGetMaximumSizeForEncoding(nint length, uint encoding);

    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CFStringGetCString(
        IntPtr value,
        byte[] buffer,
        nint bufferSize,
        uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDictionaryCreate(
        IntPtr allocator,
        IntPtr[] keys,
        IntPtr[] values,
        nint count,
        IntPtr keyCallbacks,
        IntPtr valueCallbacks);

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(IntPtr value);

    internal static IntPtr CreateString(string value) =>
        CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding);

    internal static IntPtr CreateData(ReadOnlySpan<byte> value)
    {
        var copy = value.ToArray();
        try
        {
            return CFDataCreate(IntPtr.Zero, copy, copy.Length);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(copy);
        }
    }

    internal static byte[] CopyData(IntPtr data)
    {
        var length = checked((int)CFDataGetLength(data));
        if (length <= 0)
        {
            return [];
        }

        var result = new byte[length];
        Marshal.Copy(CFDataGetBytePtr(data), result, 0, length);
        return result;
    }

    internal static string? CopyString(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID())
        {
            return null;
        }

        var maximumBytes = CFStringGetMaximumSizeForEncoding(CFStringGetLength(value), Utf8Encoding);
        var bufferLength = checked((int)maximumBytes + 1);
        var buffer = new byte[bufferLength];
        if (!CFStringGetCString(value, buffer, buffer.Length, Utf8Encoding))
        {
            return null;
        }

        var terminator = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.UTF8.GetString(buffer, 0, terminator < 0 ? buffer.Length : terminator);
    }

    internal static IntPtr CreateDictionary(IReadOnlyList<KeyValuePair<IntPtr, IntPtr>> entries)
    {
        var keys = entries.Select(static entry => entry.Key).ToArray();
        var values = entries.Select(static entry => entry.Value).ToArray();
        return CFDictionaryCreate(
            IntPtr.Zero,
            keys,
            values,
            entries.Count,
            DictionaryCallbacks.KeyCallbacks,
            DictionaryCallbacks.ValueCallbacks);
    }

    private static class DictionaryCallbacks
    {
        private static readonly IntPtr Handle = NativeLibrary.Load(CoreFoundation);

        internal static readonly IntPtr KeyCallbacks =
            NativeLibrary.GetExport(Handle, "kCFTypeDictionaryKeyCallBacks");
        internal static readonly IntPtr ValueCallbacks =
            NativeLibrary.GetExport(Handle, "kCFTypeDictionaryValueCallBacks");
    }
}

internal sealed class CoreFoundationScope : IDisposable
{
    private readonly List<IntPtr> _owned = [];

    public IntPtr String(string value) => Own(CoreFoundationNative.CreateString(value));

    public IntPtr Data(ReadOnlySpan<byte> value) => Own(CoreFoundationNative.CreateData(value));

    public IntPtr Dictionary(params KeyValuePair<IntPtr, IntPtr>[] entries) =>
        Own(CoreFoundationNative.CreateDictionary(entries));

    public IntPtr Own(IntPtr value)
    {
        if (value == IntPtr.Zero)
        {
            throw new InvalidOperationException("Core Foundation returned a null object.");
        }

        _owned.Add(value);
        return value;
    }

    public void Dispose()
    {
        for (var index = _owned.Count - 1; index >= 0; index--)
        {
            CoreFoundationNative.CFRelease(_owned[index]);
        }

        _owned.Clear();
    }
}
