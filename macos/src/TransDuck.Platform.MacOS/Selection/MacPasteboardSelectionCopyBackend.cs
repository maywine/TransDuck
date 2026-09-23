using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TransDuck.Platform.MacOS.Interop;

namespace TransDuck.Platform.MacOS.Selection;

// Used only when the focused control does not expose its selected text through AX.
internal sealed partial class MacPasteboardSelectionCopyBackend : IMacSelectionCopyBackend
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const ulong CommandFlag = 1UL << 20;
    private const int MaxSnapshotBytes = 64 * 1024 * 1024;
    private static readonly SemaphoreSlim CopyGate = new(1, 1);

    [LibraryImport(CoreGraphics)]
    private static partial IntPtr CGEventCreateKeyboardEvent(
        IntPtr source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetFlags(IntPtr keyboardEvent, ulong flags);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventPost(uint tap, IntPtr keyboardEvent);

    public async Task<string?> ReadSelectedTextAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Pasteboard selection copying requires macOS.");
        }

        await CopyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var snapshot = CapturePasteboard();
            if (snapshot is null)
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var originalChangeCount = snapshot.ChangeCount;
            if (GetChangeCount() != originalChangeCount)
            {
                return null;
            }

            PostCopyShortcut();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                // Finish clipboard restoration even if the translation operation is cancelled.
                await Task.Delay(25).ConfigureAwait(false);
                var changeCount = GetChangeCount();
                if (changeCount == originalChangeCount)
                {
                    continue;
                }

                try
                {
                    return ReadPasteboardText();
                }
                finally
                {
                    RestorePasteboard(snapshot, changeCount);
                }
            }

            return null;
        }
        finally
        {
            CopyGate.Release();
        }
    }

    private static PasteboardSnapshot? CapturePasteboard()
    {
        using var pool = new ObjectiveCAutoreleasePool();
        var pasteboard = GeneralPasteboard();
        var changeCount = GetChangeCount(pasteboard);
        var items = ObjectiveCNative.SendIntPtr(pasteboard, Selectors.PasteboardItems);
        var itemCount = items == IntPtr.Zero ? 0 : checked((int)ObjectiveCNative.SendUIntPtr(items, Selectors.Count));
        var saved = new List<List<PasteboardData>>(itemCount);
        var totalBytes = 0;
        try
        {
            for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
            {
                var item = ObjectiveCNative.SendIntPtr(items, Selectors.ObjectAtIndex, (nuint)itemIndex);
                var types = ObjectiveCNative.SendIntPtr(item, Selectors.Types);
                var typeCount = checked((int)ObjectiveCNative.SendUIntPtr(types, Selectors.Count));
                var savedTypes = new List<PasteboardData>(typeCount);
                saved.Add(savedTypes);
                for (var typeIndex = 0; typeIndex < typeCount; typeIndex++)
                {
                    var nativeType = ObjectiveCNative.SendIntPtr(types, Selectors.ObjectAtIndex, (nuint)typeIndex);
                    var type = CoreFoundationNative.CopyString(nativeType);
                    var nativeData = ObjectiveCNative.SendIntPtr(item, Selectors.DataForType, nativeType);
                    if (type is null || nativeData == IntPtr.Zero)
                    {
                        Zero(saved);
                        return null;
                    }

                    var length = checked((int)ObjectiveCNative.SendUIntPtr(nativeData, Selectors.Length));
                    if (length > MaxSnapshotBytes - totalBytes)
                    {
                        Zero(saved);
                        return null;
                    }

                    var bytes = new byte[length];
                    if (length > 0)
                    {
                        Marshal.Copy(ObjectiveCNative.SendIntPtr(nativeData, Selectors.Bytes), bytes, 0, length);
                    }

                    savedTypes.Add(new PasteboardData(type, bytes));
                    totalBytes += length;
                }
            }
        }
        catch
        {
            Zero(saved);
            throw;
        }

        if (GetChangeCount(pasteboard) != changeCount)
        {
            Zero(saved);
            return null;
        }

        return new PasteboardSnapshot(saved, changeCount);
    }

    private static void RestorePasteboard(PasteboardSnapshot snapshot, nint expectedChangeCount)
    {
        using var pool = new ObjectiveCAutoreleasePool();
        using var scope = new CoreFoundationScope();
        var pasteboard = GeneralPasteboard();
        if (GetChangeCount(pasteboard) != expectedChangeCount)
        {
            return; // Another application changed the clipboard after our copy.
        }

        var nativeItems = ObjectiveCNative.SendIntPtr(GetClass("NSMutableArray"), Selectors.Array);
        var retainedItems = new List<IntPtr>(snapshot.Items.Count);
        try
        {
            foreach (var item in snapshot.Items)
            {
                var nativeItem = ObjectiveCNative.SendIntPtr(
                    ObjectiveCNative.SendIntPtr(GetClass("NSPasteboardItem"), ObjectiveCAutoreleasePool.Selectors.Alloc),
                    ObjectiveCAutoreleasePool.Selectors.Init);
                if (nativeItem == IntPtr.Zero)
                {
                    return;
                }

                retainedItems.Add(nativeItem);
                foreach (var entry in item)
                {
                    var nativeType = scope.String(entry.Type);
                    var nativeData = ObjectiveCNative.SendIntPtr(
                        GetClass("NSData"), Selectors.DataWithBytesLength, entry.Data, (nuint)entry.Data.Length);
                    if (nativeData == IntPtr.Zero || !ObjectiveCNative.SendBool(
                            nativeItem, Selectors.SetDataForType, nativeData, nativeType))
                    {
                        return;
                    }
                }

                ObjectiveCNative.SendVoidIntPtr(nativeItems, Selectors.AddObject, nativeItem);
            }

            _ = ObjectiveCNative.SendUIntPtr(pasteboard, Selectors.ClearContents);
            if (snapshot.Items.Count > 0)
            {
                _ = ObjectiveCNative.SendBool(pasteboard, Selectors.WriteObjects, nativeItems);
            }
        }
        finally
        {
            foreach (var nativeItem in retainedItems)
            {
                ObjectiveCNative.SendVoid(nativeItem, ObjectiveCAutoreleasePool.Selectors.Release);
            }
        }
    }

    private static string? ReadPasteboardText()
    {
        using var pool = new ObjectiveCAutoreleasePool();
        using var scope = new CoreFoundationScope();
        var type = scope.String("public.utf8-plain-text");
        var text = ObjectiveCNative.SendIntPtr(GeneralPasteboard(), Selectors.StringForType, type);
        return CoreFoundationNative.CopyString(text);
    }

    private static nint GetChangeCount()
    {
        using var pool = new ObjectiveCAutoreleasePool();
        return GetChangeCount(GeneralPasteboard());
    }

    private static nint GetChangeCount(IntPtr pasteboard) =>
        checked((nint)ObjectiveCNative.SendUIntPtr(pasteboard, Selectors.ChangeCount));

    private static IntPtr GeneralPasteboard() =>
        ObjectiveCNative.SendIntPtr(GetClass("NSPasteboard"), Selectors.GeneralPasteboard);

    private static IntPtr GetClass(string name)
    {
        var result = ObjectiveCNative.objc_getClass(name);
        return result == IntPtr.Zero
            ? throw new InvalidOperationException($"The macOS {name} class is unavailable.")
            : result;
    }

    private static void PostCopyShortcut()
    {
        // 0x08 is the macOS virtual key code for C; CGEventPost uses the HID event tap.
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, 0x08, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, 0x08, false);
        if (down == IntPtr.Zero || up == IntPtr.Zero)
        {
            if (down != IntPtr.Zero) CoreFoundationNative.CFRelease(down);
            if (up != IntPtr.Zero) CoreFoundationNative.CFRelease(up);
            throw new InvalidOperationException("Could not create the copy shortcut events.");
        }

        try
        {
            CGEventSetFlags(down, CommandFlag);
            CGEventSetFlags(up, CommandFlag);
            CGEventPost(0, down);
            CGEventPost(0, up);
        }
        finally
        {
            CoreFoundationNative.CFRelease(down);
            CoreFoundationNative.CFRelease(up);
        }
    }

    private static void Zero(IEnumerable<List<PasteboardData>> items)
    {
        foreach (var item in items)
        foreach (var entry in item)
        {
            CryptographicOperations.ZeroMemory(entry.Data);
        }
    }

    private sealed record PasteboardData(string Type, byte[] Data);

    private sealed class PasteboardSnapshot(List<List<PasteboardData>> items, nint changeCount) : IDisposable
    {
        public List<List<PasteboardData>> Items { get; } = items;

        public nint ChangeCount { get; } = changeCount;

        public void Dispose() => Zero(Items);
    }

    private static class Selectors
    {
        internal static readonly IntPtr AddObject = ObjectiveCNative.sel_registerName("addObject:");
        internal static readonly IntPtr Array = ObjectiveCNative.sel_registerName("array");
        internal static readonly IntPtr Bytes = ObjectiveCNative.sel_registerName("bytes");
        internal static readonly IntPtr ChangeCount = ObjectiveCNative.sel_registerName("changeCount");
        internal static readonly IntPtr ClearContents = ObjectiveCNative.sel_registerName("clearContents");
        internal static readonly IntPtr Count = ObjectiveCNative.sel_registerName("count");
        internal static readonly IntPtr DataForType = ObjectiveCNative.sel_registerName("dataForType:");
        internal static readonly IntPtr DataWithBytesLength = ObjectiveCNative.sel_registerName("dataWithBytes:length:");
        internal static readonly IntPtr GeneralPasteboard = ObjectiveCNative.sel_registerName("generalPasteboard");
        internal static readonly IntPtr Length = ObjectiveCNative.sel_registerName("length");
        internal static readonly IntPtr ObjectAtIndex = ObjectiveCNative.sel_registerName("objectAtIndex:");
        internal static readonly IntPtr PasteboardItems = ObjectiveCNative.sel_registerName("pasteboardItems");
        internal static readonly IntPtr SetDataForType = ObjectiveCNative.sel_registerName("setData:forType:");
        internal static readonly IntPtr StringForType = ObjectiveCNative.sel_registerName("stringForType:");
        internal static readonly IntPtr Types = ObjectiveCNative.sel_registerName("types");
        internal static readonly IntPtr WriteObjects = ObjectiveCNative.sel_registerName("writeObjects:");
    }
}
