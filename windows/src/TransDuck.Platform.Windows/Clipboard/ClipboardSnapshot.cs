using System.Runtime.InteropServices;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Clipboard;

/// <summary>
/// Preserves raw, self-contained clipboard format IDs without introducing framework or OLE wrapper formats on restore.
/// HGLOBAL data is copied into managed bytes. Copied bitmap handles remain owned by this snapshot
/// until SetClipboardData accepts ownership or the snapshot is disposed.
/// </summary>
public sealed class ClipboardSnapshot : IDisposable
{
    private static readonly HashSet<uint> SelfContainedGlobalFormats =
    [
        Win32ClipboardNative.CfText,
        Win32ClipboardNative.CfSylk,
        Win32ClipboardNative.CfDif,
        Win32ClipboardNative.CfTiff,
        Win32ClipboardNative.CfOemText,
        Win32ClipboardNative.CfDib,
        Win32ClipboardNative.CfPenData,
        Win32ClipboardNative.CfRiff,
        Win32ClipboardNative.CfWave,
        Win32ClipboardNative.CfUnicodeText,
        Win32ClipboardNative.CfHDrop,
        Win32ClipboardNative.CfLocale,
        Win32ClipboardNative.CfDibV5,
    ];
    private static readonly HashSet<string> SelfContainedRegisteredFormats =
    new(StringComparer.OrdinalIgnoreCase)
    {
        "HTML Format",
        "Rich Text Format",
    };
    private readonly IReadOnlyList<ClipboardEntry> _entries;
    private bool _disposed;

    private ClipboardSnapshot(
        IReadOnlyList<ClipboardEntry> entries,
        IReadOnlyList<string> unsupportedFormatNames,
        IReadOnlyList<string> unsupportedFormatDiagnostics)
    {
        _entries = entries;
        UnsupportedFormatNames = unsupportedFormatNames;
        UnsupportedFormatDiagnostics = unsupportedFormatDiagnostics;
    }

    public IReadOnlyList<string> UnsupportedFormatNames { get; }

    public IReadOnlyList<string> UnsupportedFormatDiagnostics { get; }

    public bool HasUnsupportedFormats => UnsupportedFormatNames.Count > 0;

    public static ClipboardSnapshotResult TryCapture()
    {
        if (!Win32ClipboardNative.TryOpenClipboard(out var openError))
        {
            return ClipboardSnapshotResult.Failed(
                $"无法打开剪贴板（Win32 错误 {openError}）。");
        }

        var entries = new List<ClipboardEntry>();
        try
        {
            var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var diagnostics = new List<string>();
            var format = 0u;
            while ((format = Win32ClipboardNative.EnumClipboardFormats(format)) != 0)
            {
                var formatName = Win32ClipboardNative.GetFormatName(format);
                var handle = Win32ClipboardNative.GetClipboardData(format);
                if (handle == IntPtr.Zero)
                {
                    unsupported.Add(formatName);
                    diagnostics.Add($"{formatName}: GetClipboardData 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。");
                    continue;
                }

                if (format == Win32ClipboardNative.CfBitmap)
                {
                    if (TryCopyBitmap(format, formatName, handle, out var bitmapEntry, out var bitmapError))
                    {
                        entries.Add(bitmapEntry!);
                    }
                    else
                    {
                        unsupported.Add(formatName);
                        diagnostics.Add($"{formatName}: {bitmapError}");
                    }

                    continue;
                }

                if (!IsSelfContainedGlobalFormat(format, formatName))
                {
                    unsupported.Add(formatName);
                    diagnostics.Add($"{formatName}: 非自包含或未知剪贴板格式。");
                    continue;
                }

                // Only documented self-contained formats are copied as HGLOBAL byte payloads.
                if (TryCopyGlobalMemory(handle, out var data, out var memoryError))
                {
                    entries.Add(new GlobalMemoryClipboardEntry(format, formatName, data!));
                }
                else
                {
                    // Unknown non-HGLOBAL handles, delayed data and unsafe handle types are rejected.
                    unsupported.Add(formatName);
                    diagnostics.Add($"{formatName}: {memoryError}");
                }
            }

            return ClipboardSnapshotResult.Captured(new ClipboardSnapshot(
                entries,
                unsupported.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
                diagnostics));
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            DisposeEntries(entries);
            return ClipboardSnapshotResult.Failed($"无法读取剪贴板：{exception.Message}");
        }
        finally
        {
            Win32ClipboardNative.CloseClipboard();
        }
    }

    public ClipboardRestoreResult TryRestore()
    {
        if (_disposed)
        {
            return ClipboardRestoreResult.Failed(UnsupportedFormatNames, "剪贴板快照已释放。");
        }

        var allocations = new List<AllocatedGlobalMemory>();
        foreach (var entry in _entries.OfType<GlobalMemoryClipboardEntry>())
        {
            if (!TryAllocateGlobalMemory(entry.Data, out var handle, out var errorMessage))
            {
                DisposeAllocations(allocations);
                return ClipboardRestoreResult.Failed(
                    UnsupportedFormatNames,
                    $"无法准备 {entry.Name} 的剪贴板数据：{errorMessage}");
            }

            allocations.Add(new AllocatedGlobalMemory(handle));
        }

        if (!ClipboardOwnerWindow.TryCreate(out var owner, out var ownerError))
        {
            DisposeAllocations(allocations);
            return ClipboardRestoreResult.Failed(
                UnsupportedFormatNames,
                $"无法创建剪贴板 owner 窗口（Win32 错误 {ownerError}）。");
        }

        using var ownerWindow = owner!;
        if (!Win32ClipboardNative.TryOpenClipboard(ownerWindow.Handle, out var openError))
        {
            DisposeAllocations(allocations);
            return ClipboardRestoreResult.Failed(
                UnsupportedFormatNames,
                $"无法打开剪贴板以恢复数据（Win32 错误 {openError}）。");
        }

        var failures = new List<string>();
        try
        {
            if (!Win32ClipboardNative.EmptyClipboard())
            {
                return ClipboardRestoreResult.Failed(
                    UnsupportedFormatNames,
                    $"无法清空剪贴板以恢复数据（Win32 错误 {Marshal.GetLastWin32Error()}）。");
            }

            var allocationIndex = 0;
            foreach (var entry in _entries)
            {
                switch (entry)
                {
                    case GlobalMemoryClipboardEntry:
                        var allocation = allocations[allocationIndex++];
                        if (Win32ClipboardNative.SetClipboardData(entry.Format, allocation.Handle) == IntPtr.Zero)
                        {
                            failures.Add(DescribeSetFailure(entry));
                        }
                        else
                        {
                            allocation.TransferOwnership();
                        }

                        break;
                    case BitmapClipboardEntry bitmap:
                        if (bitmap.Handle == IntPtr.Zero ||
                            Win32ClipboardNative.SetClipboardData(entry.Format, bitmap.Handle) == IntPtr.Zero)
                        {
                            failures.Add(DescribeSetFailure(entry));
                        }
                        else
                        {
                            bitmap.TransferOwnership();
                        }

                        break;
                }
            }
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            failures.Add($"恢复过程异常：{exception.Message}");
        }
        finally
        {
            Win32ClipboardNative.CloseClipboard();
            DisposeAllocations(allocations);
        }

        return failures.Count == 0
            ? ClipboardRestoreResult.Succeeded(UnsupportedFormatNames)
            : ClipboardRestoreResult.Failed(
                UnsupportedFormatNames,
                "无法恢复全部剪贴板格式：" + string.Join("；", failures));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _entries)
        {
            entry.Dispose();
        }
    }

    private static bool TryCopyBitmap(
        uint format,
        string name,
        IntPtr sourceHandle,
        out BitmapClipboardEntry? entry,
        out string errorMessage)
    {
        var copiedHandle = Win32ClipboardNative.CopyImage(
            sourceHandle,
            Win32ClipboardNative.ImageBitmap,
            0,
            0,
            Win32ClipboardNative.LrCreatedibSection);
        if (copiedHandle == IntPtr.Zero)
        {
            entry = null;
            errorMessage = $"CopyImage 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        entry = new BitmapClipboardEntry(format, name, copiedHandle);
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryCopyGlobalMemory(
        IntPtr sourceHandle,
        out byte[]? data,
        out string errorMessage)
    {
        data = null;
        var nativeSize = Win32ClipboardNative.GlobalSize(sourceHandle).ToUInt64();
        if (nativeSize == 0 || nativeSize > int.MaxValue)
        {
            errorMessage = nativeSize == 0
                ? $"GlobalSize 无法识别 HGLOBAL（Win32 错误 {Marshal.GetLastWin32Error()}）。"
                : "HGLOBAL 大小超过可安全复制的范围。";
            return false;
        }

        var source = Win32ClipboardNative.GlobalLock(sourceHandle);
        if (source == IntPtr.Zero)
        {
            errorMessage = $"GlobalLock 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        string? unlockError = null;
        var copy = new byte[(int)nativeSize];
        try
        {
            Marshal.Copy(source, copy, 0, copy.Length);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            errorMessage = $"复制 HGLOBAL 失败：{exception.Message}";
            return false;
        }
        finally
        {
            if (!Win32ClipboardNative.GlobalUnlock(sourceHandle) && Marshal.GetLastWin32Error() != 0)
            {
                unlockError = "无法解锁全局内存。";
            }
        }

        if (unlockError is not null)
        {
            errorMessage = unlockError;
            return false;
        }

        data = copy;
        errorMessage = string.Empty;
        return true;
    }

    private static bool TryAllocateGlobalMemory(
        byte[] data,
        out IntPtr handle,
        out string errorMessage)
    {
        handle = Win32ClipboardNative.GlobalAlloc(Win32ClipboardNative.GmemMoveable, (UIntPtr)data.Length);
        if (handle == IntPtr.Zero)
        {
            errorMessage = $"GlobalAlloc 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        var destination = Win32ClipboardNative.GlobalLock(handle);
        if (destination == IntPtr.Zero)
        {
            Win32ClipboardNative.GlobalFree(handle);
            handle = IntPtr.Zero;
            errorMessage = $"GlobalLock 失败（Win32 错误 {Marshal.GetLastWin32Error()}）。";
            return false;
        }

        var copyError = string.Empty;
        var unlockError = 0;
        try
        {
            Marshal.Copy(data, 0, destination, data.Length);
        }
        catch (Exception exception) when (!IsFatal(exception))
        {
            copyError = exception.Message;
        }
        finally
        {
            if (!Win32ClipboardNative.GlobalUnlock(handle))
            {
                unlockError = Marshal.GetLastWin32Error();
            }
        }

        if (!string.IsNullOrEmpty(copyError) || unlockError != 0)
        {
            Win32ClipboardNative.GlobalFree(handle);
            handle = IntPtr.Zero;
            errorMessage = !string.IsNullOrEmpty(copyError)
                ? copyError
                : $"GlobalUnlock 失败（Win32 错误 {unlockError}）。";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    private static string DescribeSetFailure(ClipboardEntry entry) =>
        $"{entry.Name}（Win32 错误 {Marshal.GetLastWin32Error()}）";

    private static bool IsSelfContainedGlobalFormat(uint format, string formatName) =>
        SelfContainedGlobalFormats.Contains(format) ||
        format >= 0xC000 && SelfContainedRegisteredFormats.Contains(formatName);

    private static void DisposeAllocations(IEnumerable<AllocatedGlobalMemory> allocations)
    {
        foreach (var allocation in allocations)
        {
            allocation.Dispose();
        }
    }

    private static void DisposeEntries(IEnumerable<ClipboardEntry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Dispose();
        }
    }

    private static bool IsFatal(Exception exception) => exception is
        OutOfMemoryException or
        StackOverflowException or
        AccessViolationException;

}

public sealed record ClipboardSnapshotResult(ClipboardSnapshot? Snapshot, string? ErrorMessage)
{
    public bool Succeeded => Snapshot is not null;

    public static ClipboardSnapshotResult Captured(ClipboardSnapshot snapshot) => new(snapshot, null);

    public static ClipboardSnapshotResult Failed(string errorMessage) => new(null, errorMessage);
}

public sealed record ClipboardRestoreResult(
    bool WasRestored,
    IReadOnlyList<string> UnsupportedFormatNames,
    string? ErrorMessage)
{
    public static ClipboardRestoreResult Succeeded(IReadOnlyList<string> unsupportedFormatNames) =>
        new(true, unsupportedFormatNames, null);

    public static ClipboardRestoreResult Failed(
        IReadOnlyList<string> unsupportedFormatNames,
        string errorMessage) => new(false, unsupportedFormatNames, errorMessage);
}
