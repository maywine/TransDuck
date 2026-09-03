using Avalonia.Controls;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;
using TransDuck.UI;
using TransDuck.UI.Views;

namespace TransDuck.MacOS.App.Views;

internal sealed class HistoryWindow : HistoryWindowBase
{
    private readonly MacAppRuntime _runtime;
    private bool _allowClose;

    public HistoryWindow(MacAppRuntime runtime)
    {
        _runtime = runtime;
        ConfigureForMacHistoryWindow();
        RefreshRequested += HandleRefreshRequested;
        ClearRequested += HandleClearRequested;
        CloseRequested += HandleCloseRequested;
        Opened += HandleOpened;
        Closing += HandleClosing;
    }

    private void HandleOpened(object? sender, EventArgs eventArgs) => _ = RefreshAsync();

    private void HandleRefreshRequested(object? sender, EventArgs eventArgs) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusTextBlock.Text = "Loading history...";
        var result = await _runtime.LoadHistoryAsync(CancellationToken.None);
        if (result.Status == PersistenceStatus.NotFound)
        {
            SetHistoryItems([]);
            StatusTextBlock.Text = "History is empty.";
            return;
        }

        if (!result.Succeeded)
        {
            SetHistoryItems([]);
            StatusTextBlock.Text = "History could not be loaded.";
            return;
        }

        SetHistoryItems(result.Entries.Select(CreatePresentation).ToArray());

        StatusTextBlock.Text = result.CorruptLineCount > 0
            ? $"Loaded history; ignored {result.CorruptLineCount} corrupt record(s)."
            : $"Loaded {result.Entries.Count} record(s).";
    }

    private async void HandleClearRequested(object? sender, EventArgs eventArgs)
    {
        var result = await _runtime.ClearHistoryAsync(CancellationToken.None);
        StatusTextBlock.Text = result.Status is PersistenceStatus.Succeeded or PersistenceStatus.NotFound
            ? "History cleared."
            : "History could not be cleared.";
        await RefreshAsync();
    }

    private void HandleCloseRequested(object? sender, EventArgs eventArgs) => Close();

    private static HistoryItemViewModel CreatePresentation(HistoryEntry entry)
    {
        var result = entry.Result.TerminalState switch
        {
            QueryTerminalState.Completed => entry.Result.Result?.Text ?? string.Empty,
            QueryTerminalState.Cancelled => "[cancelled]",
            QueryTerminalState.Failed => "[failed: " + entry.Result.Error?.Code + "]",
            _ => "[unknown]",
        };
        var provider = LocalDictionaryIds.IsFile(entry.Request.Provider.ProviderId)
            ? "Local dictionary"
            : entry.Request.Provider.ProviderId;
        var source = entry.Request.Text;
        var summary = source.ReplaceLineEndings(" ").Trim();
        if (summary.Length > 72)
        {
            summary = summary[..72] + "…";
        }

        return new HistoryItemViewModel(
            $"{entry.CreatedAt.ToLocalTime():g} · {provider} · {summary}",
            source,
            result);
    }

    private void HandleClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (!_allowClose)
        {
            eventArgs.Cancel = true;
            Hide();
        }
    }

    internal void PrepareForShutdown() => _allowClose = true;
}
