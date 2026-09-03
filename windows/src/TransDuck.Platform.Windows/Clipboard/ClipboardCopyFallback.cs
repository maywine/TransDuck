using System.Runtime.InteropServices;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Clipboard;

/// <summary>
/// Reads a selection by sending Ctrl+C while avoiding a permanent overwrite of supported clipboard data.
/// </summary>
public sealed class ClipboardCopyFallback
{
    private const int ModifierReleaseTimeoutMilliseconds = 300;
    private const int ModifierReleasePollMilliseconds = 15;
    private const int CopyWaitMilliseconds = 1000;
    private static readonly (int VirtualKey, string Name)[] ModifierKeys =
    [
        (Win32InputNative.VkControl, "Ctrl"),
        (Win32InputNative.VkMenu, "Alt"),
        (Win32InputNative.VkShift, "Shift"),
        (Win32InputNative.VkLWin, "Left Win"),
        (Win32InputNative.VkRWin, "Right Win"),
    ];

    public async Task<ClipboardCopyResult> TryReadAsync(CancellationToken cancellationToken)
    {
        var capture = ClipboardSnapshot.TryCapture();
        if (!capture.Succeeded)
        {
            return ClipboardCopyResult.Failed(
                capture.ErrorMessage ?? "无法备份剪贴板。",
                [],
                ClipboardRestorationResult.NotAttempted());
        }

        using var snapshot = capture.Snapshot!;
        if (snapshot.HasUnsupportedFormats)
        {
            return ClipboardCopyResult.Failed(
                "为避免覆盖无法无损恢复的剪贴板格式，已取消复制回退：" +
                DescribeUnsupportedFormats(snapshot.UnsupportedFormatDiagnostics.Count == 0
                    ? snapshot.UnsupportedFormatNames
                    : snapshot.UnsupportedFormatDiagnostics),
                snapshot.UnsupportedFormatNames,
                ClipboardRestorationResult.NotAttempted());
        }

        var snapshotSequence = Win32ClipboardNative.GetClipboardSequenceNumber();

        try
        {
            // WM_HOTKEY may arrive before the user physically releases Ctrl+Alt+D.
            var modifierRelease = await WaitForModifierReleaseAsync(cancellationToken);
            if (!modifierRelease.IsReleased)
            {
                var detail = modifierRelease.PressedModifiers.Count == 0
                    ? "等待修饰键释放超时"
                    : "修饰键仍处于按下状态：" +
                      string.Join(", ", modifierRelease.PressedModifiers);
                return ClipboardCopyResult.Failed(
                    detail + "，已取消复制回退。",
                    snapshot.UnsupportedFormatNames,
                    ClipboardRestorationResult.NotAttempted());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ClipboardCopyResult.Cancelled(
                snapshot.UnsupportedFormatNames,
                ClipboardRestorationResult.NotAttempted());
        }

        if (Win32ClipboardNative.GetClipboardSequenceNumber() != snapshotSequence)
        {
            return ClipboardCopyResult.Failed(
                "剪贴板在复制回退开始前已被其他进程更新，已取消以避免覆盖该更新。",
                snapshot.UnsupportedFormatNames,
                ClipboardRestorationResult.NotAttempted());
        }

        var markerSequence = 0u;
        var copySequence = 0u;
        var markerWasWritten = false;
        var copyWasObserved = false;
        var result = ClipboardCopyResult.Failed(
            "复制回退未完成。",
            snapshot.UnsupportedFormatNames,
            ClipboardRestorationResult.NotAttempted());
        try
        {
            if (!Win32ClipboardNative.TryEmptyClipboard(out var clearError))
            {
                return ClipboardCopyResult.Failed(
                    $"无法清空剪贴板以执行复制回退（Win32 错误 {clearError}）。",
                    snapshot.UnsupportedFormatNames,
                    ClipboardRestorationResult.NotAttempted());
            }

            markerSequence = Win32ClipboardNative.GetClipboardSequenceNumber();
            markerWasWritten = true;

            var sendResult = SendCopyShortcut();
            if (!sendResult.Succeeded)
            {
                var copyFailure = sendResult.InputError == 0
                    ? "目标窗口未接受复制输入，可能受 UIPI 或控件策略限制。"
                    : $"无法发送复制输入（Win32 错误 {sendResult.InputError}）。";
                result = ClipboardCopyResult.Failed(
                    copyFailure + sendResult.ReleaseDiagnostic,
                    snapshot.UnsupportedFormatNames,
                    ClipboardRestorationResult.NotAttempted());
            }
            else
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(CopyWaitMilliseconds);
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    copySequence = Win32ClipboardNative.GetClipboardSequenceNumber();
                    if (copySequence != markerSequence)
                    {
                        copyWasObserved = true;
                        break;
                    }

                    await Task.Delay(20, cancellationToken).ConfigureAwait(true);
                }
                while (DateTime.UtcNow < deadline);

                if (!copyWasObserved)
                {
                    result = ClipboardCopyResult.Failed(
                        "复制超时：目标控件没有提供可读取的剪贴板文本。",
                        snapshot.UnsupportedFormatNames,
                        ClipboardRestorationResult.NotAttempted());
                }
                else
                {
                    WindowsClipboardText.TryRead(out var text, out _);

                    result = string.IsNullOrEmpty(text)
                        ? ClipboardCopyResult.Failed(
                            "复制结果不是文本或当前选区为空。",
                            snapshot.UnsupportedFormatNames,
                            ClipboardRestorationResult.NotAttempted())
                        : ClipboardCopyResult.Succeeded(
                            text,
                            snapshot.UnsupportedFormatNames,
                            ClipboardRestorationResult.NotAttempted());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            result = ClipboardCopyResult.Cancelled(
                snapshot.UnsupportedFormatNames,
                ClipboardRestorationResult.NotAttempted());
        }
        catch (ExternalException exception)
        {
            result = ClipboardCopyResult.Failed(
                $"无法读取复制结果：{exception.Message}",
                snapshot.UnsupportedFormatNames,
                ClipboardRestorationResult.NotAttempted());
        }
        catch (Exception exception)
        {
            result = ClipboardCopyResult.Failed(
                $"复制回退发生未预期错误：{exception.Message}",
                snapshot.UnsupportedFormatNames,
                ClipboardRestorationResult.NotAttempted());
        }

        if (!markerWasWritten)
        {
            return result;
        }

        var currentSequence = Win32ClipboardNative.GetClipboardSequenceNumber();
        var restoration = currentSequence == markerSequence ||
                          (copyWasObserved && currentSequence == copySequence)
            ? ToRestorationResult(snapshot.TryRestore())
            : ClipboardRestorationResult.SkippedForConcurrentChange(
                snapshot.UnsupportedFormatNames,
                "检测到其他进程在复制期间更新了剪贴板，已跳过恢复以避免覆盖该更新。");

        return result with { Restoration = restoration };
    }

    private static ClipboardRestorationResult ToRestorationResult(ClipboardRestoreResult restore) =>
        restore.WasRestored
            ? ClipboardRestorationResult.Restored(restore.UnsupportedFormatNames)
            : ClipboardRestorationResult.Failed(
                restore.UnsupportedFormatNames,
                restore.ErrorMessage ?? "无法恢复剪贴板。");

    private static string DescribeUnsupportedFormats(IReadOnlyList<string> formats)
    {
        const int maximumFormats = 6;
        const int maximumFormatNameLength = 64;
        var visibleFormats = formats
            .Take(maximumFormats)
            .Select(format => format.Length > maximumFormatNameLength
                ? format[..maximumFormatNameLength] + "…"
                : format);
        var suffix = formats.Count > maximumFormats ? $" 等共 {formats.Count} 项" : string.Empty;
        return string.Join(", ", visibleFormats) + suffix + "。";
    }

    private static async Task<ModifierReleaseResult> WaitForModifierReleaseAsync(
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ModifierReleaseTimeoutMilliseconds);
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pressedModifiers = GetPressedModifiers();
            if (pressedModifiers.Count == 0)
            {
                return ModifierReleaseResult.Success();
            }

            await Task.Delay(ModifierReleasePollMilliseconds, cancellationToken).ConfigureAwait(true);
        }
        while (DateTime.UtcNow < deadline);

        return ModifierReleaseResult.TimedOut(GetPressedModifiers());
    }

    private static IReadOnlyList<string> GetPressedModifiers() => ModifierKeys
        .Where(modifier => IsPressed(modifier.VirtualKey))
        .Select(modifier => modifier.Name)
        .ToArray();

    private static bool IsPressed(int virtualKey) =>
        (Win32InputNative.GetAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;

    private static SendCopyShortcutResult SendCopyShortcut()
    {
        var inputs = new[]
        {
            KeyboardInput(Win32InputNative.VkControl, 0),
            KeyboardInput(Win32InputNative.VkC, 0),
            KeyboardInput(Win32InputNative.VkC, Win32InputNative.KeyEventKeyUp),
            KeyboardInput(Win32InputNative.VkControl, Win32InputNative.KeyEventKeyUp),
        };

        var inputCount = Win32InputNative.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeInput>());
        if (inputCount == inputs.Length)
        {
            return SendCopyShortcutResult.Success();
        }

        var inputError = Marshal.GetLastWin32Error();
        var cReleaseError = SendKeyUp(Win32InputNative.VkC);
        var controlReleaseError = SendKeyUp(Win32InputNative.VkControl);
        return SendCopyShortcutResult.Failed(inputError, cReleaseError, controlReleaseError);
    }

    private static int? SendKeyUp(ushort virtualKey)
    {
        var keyUp = new[] { KeyboardInput(virtualKey, Win32InputNative.KeyEventKeyUp) };
        return Win32InputNative.SendInput(1, keyUp, Marshal.SizeOf<NativeInput>()) == 1
            ? null
            : Marshal.GetLastWin32Error();
    }

    private static NativeInput KeyboardInput(ushort virtualKey, uint flags) => new()
    {
        Type = Win32InputNative.InputKeyboard,
        Data = new NativeInputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = virtualKey,
                Flags = flags,
            },
        },
    };
}

internal sealed record SendCopyShortcutResult(
    bool Succeeded,
    int InputError,
    int? CReleaseError,
    int? ControlReleaseError)
{
    public string ReleaseDiagnostic => CReleaseError is null && ControlReleaseError is null
        ? " 已尝试单独发送 C 与 Ctrl 的 key-up 事件。"
        : $" C key-up {DescribeError(CReleaseError)}；Ctrl key-up {DescribeError(ControlReleaseError)}。";

    public static SendCopyShortcutResult Success() => new(true, 0, null, null);

    public static SendCopyShortcutResult Failed(
        int inputError,
        int? cReleaseError,
        int? controlReleaseError) => new(false, inputError, cReleaseError, controlReleaseError);

    private static string DescribeError(int? error) => error is null
        ? "已发送"
        : error == 0
            ? "未发送（没有 Win32 错误）"
            : $"未发送（Win32 错误 {error}）";
}

internal sealed record ModifierReleaseResult(bool IsReleased, IReadOnlyList<string> PressedModifiers)
{
    public static ModifierReleaseResult Success() => new(true, []);

    public static ModifierReleaseResult TimedOut(IReadOnlyList<string> pressedModifiers) =>
        new(false, pressedModifiers);
}

public sealed record ClipboardCopyResult(
    string? Text,
    ClipboardCopyStatus Status,
    string? ErrorMessage,
    IReadOnlyList<string> UnsupportedClipboardFormats,
    ClipboardRestorationResult Restoration)
{
    public static ClipboardCopyResult Succeeded(
        string text,
        IReadOnlyList<string> unsupportedFormats,
        ClipboardRestorationResult restoration) =>
        new(text, ClipboardCopyStatus.Succeeded, null, unsupportedFormats, restoration);

    public static ClipboardCopyResult Failed(
        string message,
        IReadOnlyList<string> unsupportedFormats,
        ClipboardRestorationResult restoration) =>
        new(null, ClipboardCopyStatus.Failed, message, unsupportedFormats, restoration);

    public static ClipboardCopyResult Cancelled(
        IReadOnlyList<string> unsupportedFormats,
        ClipboardRestorationResult restoration) =>
        new(null, ClipboardCopyStatus.Cancelled, null, unsupportedFormats, restoration);
}

public sealed record ClipboardRestorationResult(
    ClipboardRestorationStatus Status,
    IReadOnlyList<string> UnsupportedClipboardFormats,
    string? ErrorMessage = null)
{
    public static ClipboardRestorationResult Restored(IReadOnlyList<string> unsupportedFormats) =>
        new(ClipboardRestorationStatus.Restored, unsupportedFormats);

    public static ClipboardRestorationResult SkippedForConcurrentChange(
        IReadOnlyList<string> unsupportedFormats,
        string message) => new(ClipboardRestorationStatus.SkippedForConcurrentChange, unsupportedFormats, message);

    public static ClipboardRestorationResult Failed(
        IReadOnlyList<string> unsupportedFormats,
        string message) => new(ClipboardRestorationStatus.Failed, unsupportedFormats, message);

    public static ClipboardRestorationResult NotAttempted() =>
        new(ClipboardRestorationStatus.NotAttempted, []);
}

public enum ClipboardCopyStatus
{
    Succeeded,
    Failed,
    Cancelled,
}

public enum ClipboardRestorationStatus
{
    Restored,
    SkippedForConcurrentChange,
    Failed,
    NotAttempted,
}
