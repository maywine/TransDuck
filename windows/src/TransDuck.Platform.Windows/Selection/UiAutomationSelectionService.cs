using System.Runtime.InteropServices;
using System.Windows.Automation;
using TransDuck.Platform.Windows.Clipboard;

namespace TransDuck.Platform.Windows.Selection;

/// <summary>
/// Reads a focused control's TextPattern selection before using the controlled Ctrl+C fallback.
/// </summary>
public sealed class UiAutomationSelectionService
{
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
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is null || !focused.TryGetCurrentPattern(TextPattern.Pattern, out var rawPattern))
            {
                return SelectionReadResult.FallbackRequired("焦点控件没有公开 UI Automation TextPattern。");
            }

            var ranges = ((TextPattern)rawPattern).GetSelection();
            var text = string.Concat(ranges.Select(range => range.GetText(-1)));
            return string.IsNullOrEmpty(text)
                ? SelectionReadResult.FallbackRequired("UI Automation 返回了空选区。")
                : SelectionReadResult.FromTextPattern(text);
        }
        catch (ElementNotAvailableException)
        {
            return SelectionReadResult.FallbackRequired("焦点控件在读取过程中不可用。");
        }
        catch (COMException exception)
        {
            return SelectionReadResult.FallbackRequired($"UI Automation 访问失败：{exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return SelectionReadResult.FallbackRequired($"UI Automation 访问失败：{exception.Message}");
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
