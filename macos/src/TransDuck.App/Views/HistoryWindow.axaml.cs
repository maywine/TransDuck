using Avalonia.Controls;
using Avalonia.Interactivity;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.MacOS.App.Views;

internal partial class HistoryWindow : Window
{
    private readonly MacAppRuntime _runtime;
    private bool _allowClose;

    public HistoryWindow(MacAppRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        Opened += HandleOpened;
        Closing += HandleClosing;
    }

    private void HandleOpened(object? sender, EventArgs eventArgs) => _ = RefreshAsync();

    private void HandleRefreshClick(object? sender, RoutedEventArgs eventArgs) => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        StatusTextBlock.Text = "Loading history...";
        var result = await _runtime.LoadHistoryAsync(CancellationToken.None);
        HistoryListBox.Items.Clear();
        if (result.Status == PersistenceStatus.NotFound)
        {
            StatusTextBlock.Text = "History is empty.";
            return;
        }

        if (!result.Succeeded)
        {
            StatusTextBlock.Text = "History could not be loaded.";
            return;
        }

        foreach (var entry in result.Entries)
        {
            HistoryListBox.Items.Add(Describe(entry));
        }

        StatusTextBlock.Text = result.CorruptLineCount > 0
            ? $"Loaded history; ignored {result.CorruptLineCount} corrupt record(s)."
            : $"Loaded {result.Entries.Count} record(s).";
    }

    private async void HandleClearClick(object? sender, RoutedEventArgs eventArgs)
    {
        var result = await _runtime.ClearHistoryAsync(CancellationToken.None);
        StatusTextBlock.Text = result.Status is PersistenceStatus.Succeeded or PersistenceStatus.NotFound
            ? "History cleared."
            : "History could not be cleared.";
        await RefreshAsync();
    }

    private static string Describe(HistoryEntry entry)
    {
        var result = entry.Result.TerminalState switch
        {
            QueryTerminalState.Completed => entry.Result.Result?.Text ?? string.Empty,
            QueryTerminalState.Cancelled => "[cancelled]",
            QueryTerminalState.Failed => "[failed: " + entry.Result.Error?.Code + "]",
            _ => "[unknown]",
        };
        return $"{entry.CreatedAt.ToLocalTime():g}  {entry.Request.Provider.ProviderId}\n" +
            $"{entry.Request.Text}\n\n{result}";
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
