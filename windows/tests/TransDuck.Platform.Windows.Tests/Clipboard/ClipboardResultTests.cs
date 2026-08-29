// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Platform.Windows.Clipboard;
using TransDuck.Platform.Windows.Selection;

namespace TransDuck.Platform.Windows.Tests.Clipboard;

public sealed class ClipboardResultTests
{
    [Fact]
    public void ClipboardSuccess_PropagatesRestorationStatusToSelectionResult()
    {
        IReadOnlyList<string> unsupportedFormats = ["PrivateFormat"];
        var restoration = ClipboardRestorationResult.SkippedForConcurrentChange(
            unsupportedFormats,
            "Clipboard changed while copying.");
        var copy = ClipboardCopyResult.Succeeded("selected text", unsupportedFormats, restoration);
        var selection = SelectionReadResult.FromClipboard(
            copy.Text!,
            copy.UnsupportedClipboardFormats,
            copy.Restoration);

        Assert.Equal(ClipboardCopyStatus.Succeeded, copy.Status);
        Assert.Equal(SelectionReadPath.ClipboardCopy, selection.Path);
        Assert.Equal(SelectionReadStatus.Succeeded, selection.Status);
        Assert.Equal(ClipboardRestorationStatus.SkippedForConcurrentChange,
            selection.ClipboardRestoration.Status);
        Assert.Equal("Clipboard changed while copying.", selection.ClipboardRestoration.ErrorMessage);
        Assert.Equal(unsupportedFormats, selection.UnsupportedClipboardFormats);
    }

    [Fact]
    public void ClipboardFailureAndCancellation_PreserveRestorationOutcomes()
    {
        IReadOnlyList<string> unsupportedFormats = ["DelayedFormat"];
        var failedRestoration = ClipboardRestorationResult.Failed(
            unsupportedFormats,
            "Clipboard restore failed.");
        var failedCopy = ClipboardCopyResult.Failed(
            "Copy failed.",
            unsupportedFormats,
            failedRestoration);
        var failedSelection = SelectionReadResult.Failed(
            failedCopy.ErrorMessage!,
            failedCopy.UnsupportedClipboardFormats,
            failedCopy.Restoration);
        var cancelledCopy = ClipboardCopyResult.Cancelled(
            unsupportedFormats,
            ClipboardRestorationResult.NotAttempted());
        var cancelledSelection = SelectionReadResult.Cancelled(
            cancelledCopy.UnsupportedClipboardFormats,
            cancelledCopy.Restoration);

        Assert.Equal(ClipboardRestorationStatus.Failed, failedSelection.ClipboardRestoration.Status);
        Assert.Equal("Clipboard restore failed.", failedSelection.ClipboardRestoration.ErrorMessage);
        Assert.Equal(SelectionReadStatus.Cancelled, cancelledSelection.Status);
        Assert.Equal(ClipboardRestorationStatus.NotAttempted,
            cancelledSelection.ClipboardRestoration.Status);
    }
}
