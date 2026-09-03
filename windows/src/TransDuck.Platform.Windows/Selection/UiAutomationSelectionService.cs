using System.Runtime.InteropServices;
using System.Text;
using TransDuck.Platform.Windows.Clipboard;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Accessibility;

namespace TransDuck.Platform.Windows.Selection;

/// <summary>
/// Reads a focused control's TextPattern selection before using the controlled Ctrl+C fallback.
/// </summary>
public sealed class UiAutomationSelectionService
{
    private static readonly Guid UiAutomationClassId = new("FF48DBA4-60EF-4201-AA87-54103EEF594E");
    private readonly ClipboardCopyFallback _clipboardFallback;

    public UiAutomationSelectionService(ClipboardCopyFallback clipboardFallback)
    {
        _clipboardFallback = clipboardFallback;
    }

    public async Task<SelectionReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var automationResult = TryReadTextPattern();
        if (automationResult.Text is not null)
        {
            return automationResult;
        }

        var fallback = await _clipboardFallback.TryReadAsync(cancellationToken).ConfigureAwait(true);
        return fallback.Status switch
        {
            ClipboardCopyStatus.Succeeded => SelectionReadResult.FromClipboard(
                fallback.Text!,
                fallback.UnsupportedClipboardFormats,
                fallback.Restoration),
            ClipboardCopyStatus.Cancelled => SelectionReadResult.Cancelled(
                fallback.UnsupportedClipboardFormats,
                fallback.Restoration),
            _ => SelectionReadResult.Failed(
                fallback.ErrorMessage ?? automationResult.Detail ?? "无法读取当前选区。",
                fallback.UnsupportedClipboardFormats,
                fallback.Restoration),
        };
    }

    private static SelectionReadResult TryReadTextPattern()
    {
        IUIAutomation? automation = null;
        IUIAutomationElement? focused = null;
        object? rawPattern = null;
        IUIAutomationTextRangeArray? ranges = null;
        try
        {
            PInvoke.CoCreateInstance<IUIAutomation>(
                    in UiAutomationClassId,
                    null!,
                    CLSCTX.CLSCTX_INPROC_SERVER,
                    out automation)
                .ThrowOnFailure();
            focused = automation.GetFocusedElement();
            if (focused is null)
            {
                return SelectionReadResult.FallbackRequired("UI Automation 没有返回焦点控件。");
            }

            rawPattern = focused.GetCurrentPattern(UIA_PATTERN_ID.UIA_TextPatternId);
            if (rawPattern is not IUIAutomationTextPattern textPattern)
            {
                return SelectionReadResult.FallbackRequired("焦点控件没有公开 UI Automation TextPattern。");
            }

            ranges = textPattern.GetSelection();
            var text = new StringBuilder();
            for (var index = 0; index < ranges.Length; index++)
            {
                var range = ranges.GetElement(index);
                try
                {
                    var value = range.GetText(-1);
                    try
                    {
                        text.Append(value.ToString());
                    }
                    finally
                    {
                        PInvoke.SysFreeString(value);
                    }
                }
                finally
                {
                    ReleaseComObject(range);
                }
            }

            return text.Length == 0
                ? SelectionReadResult.FallbackRequired("UI Automation 返回了空选区。")
                : SelectionReadResult.FromTextPattern(text.ToString());
        }
        catch (COMException exception)
        {
            return SelectionReadResult.FallbackRequired($"UI Automation 访问失败：{exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return SelectionReadResult.FallbackRequired($"UI Automation 访问失败：{exception.Message}");
        }
        catch (InvalidCastException exception)
        {
            return SelectionReadResult.FallbackRequired($"UI Automation TextPattern 不可用：{exception.Message}");
        }
        finally
        {
            ReleaseComObject(ranges);
            ReleaseComObject(rawPattern);
            ReleaseComObject(focused);
            ReleaseComObject(automation);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}

public sealed record SelectionReadResult(
    string? Text,
    SelectionReadPath Path,
    SelectionReadStatus Status,
    string? Detail,
    IReadOnlyList<string> UnsupportedClipboardFormats,
    ClipboardRestorationResult ClipboardRestoration)
{
    public static SelectionReadResult FromTextPattern(string text) =>
        new(text, SelectionReadPath.TextPattern, SelectionReadStatus.Succeeded, null, [],
            ClipboardRestorationResult.NotAttempted());

    public static SelectionReadResult FromClipboard(
        string text,
        IReadOnlyList<string> unsupportedFormats,
        ClipboardRestorationResult restoration) =>
        new(text, SelectionReadPath.ClipboardCopy, SelectionReadStatus.Succeeded, null, unsupportedFormats,
            restoration);

    public static SelectionReadResult FallbackRequired(string detail) =>
        new(null, SelectionReadPath.None, SelectionReadStatus.FallbackRequired, detail, [],
            ClipboardRestorationResult.NotAttempted());

    public static SelectionReadResult Failed(
        string detail,
        IReadOnlyList<string> unsupportedFormats,
        ClipboardRestorationResult restoration) =>
        new(null, SelectionReadPath.ClipboardCopy, SelectionReadStatus.Failed, detail, unsupportedFormats,
            restoration);

    public static SelectionReadResult Cancelled(
        IReadOnlyList<string> unsupportedFormats,
        ClipboardRestorationResult restoration) =>
        new(null, SelectionReadPath.ClipboardCopy, SelectionReadStatus.Cancelled, null, unsupportedFormats,
            restoration);
}

public enum SelectionReadPath
{
    None,
    TextPattern,
    ClipboardCopy,
}

public enum SelectionReadStatus
{
    Succeeded,
    FallbackRequired,
    Failed,
    Cancelled,
}
