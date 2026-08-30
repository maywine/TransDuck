using System.Runtime.InteropServices;

namespace TransDuck.Platform.MacOS.Interop;

internal static partial class ObjectiveCNative
{
    private const string ObjectiveC = "/usr/lib/libobjc.A.dylib";

    [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr objc_getClass(string name);

    [LibraryImport(ObjectiveC, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr sel_registerName(string name);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendIntPtr(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial IntPtr SendIntPtr(IntPtr receiver, IntPtr selector, nuint value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial nuint SendUIntPtr(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static partial bool SendBool(
        IntPtr receiver,
        IntPtr selector,
        IntPtr first,
        IntPtr second);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoid(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoidIntPtr(IntPtr receiver, IntPtr selector, IntPtr value);

    [LibraryImport(ObjectiveC, EntryPoint = "objc_msgSend")]
    internal static partial void SendVoidByte(IntPtr receiver, IntPtr selector, byte value);
}

internal sealed class ObjectiveCAutoreleasePool : IDisposable
{
    private readonly IntPtr _pool;

    public ObjectiveCAutoreleasePool()
    {
        var poolClass = ObjectiveCNative.objc_getClass("NSAutoreleasePool");
        _pool = ObjectiveCNative.SendIntPtr(
            ObjectiveCNative.SendIntPtr(poolClass, Selectors.Alloc),
            Selectors.Init);
        if (_pool == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not create an Objective-C autorelease pool.");
        }
    }

    public void Dispose() => ObjectiveCNative.SendVoid(_pool, Selectors.Drain);

    internal static class Selectors
    {
        internal static readonly IntPtr Alloc = ObjectiveCNative.sel_registerName("alloc");
        internal static readonly IntPtr Init = ObjectiveCNative.sel_registerName("init");
        internal static readonly IntPtr Drain = ObjectiveCNative.sel_registerName("drain");
        internal static readonly IntPtr Release = ObjectiveCNative.sel_registerName("release");
    }
}
