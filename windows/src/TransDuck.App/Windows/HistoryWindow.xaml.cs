// Copyright (c) 2026 maywine. All rights reserved.

using System.Windows;
using System.Windows.Controls;
using TransDuck.App.Services;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;

namespace TransDuck.App.Windows;

/// <summary>
/// Presents local query history while keeping persistence and diagnostics outside WPF event handlers.
/// </summary>
public partial class HistoryWindow : Window
{
    private readonly HistoryController _controller;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private int _loadGeneration;
    private bool _isClearing;
    private bool _isClosed;
    private bool _isLoading;

    internal HistoryWindow(HistoryController controller)
    {
        _controller = controller;
        InitializeComponent();
        Loaded += HandleLoaded;
        Closed += HandleClosed;
    }

    private async void HandleLoaded(object sender, RoutedEventArgs eventArgs) => await RefreshAsync();

    private void HistorySelectionChanged(object sender, SelectionChangedEventArgs eventArgs) =>
        DisplayEntry(HistoryListBox.SelectedItem as HistoryListItem);

    private async void RefreshHistoryButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isClearing || _isClosed)
        {
            return;
        }

        await RefreshAsync();
    }

    private async void ClearHistoryButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isClearing || _isClosed)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            AppStrings.Get("history.confirm.clear_message"),
            AppStrings.Get("history.confirm.clear_title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _isClearing = true;
        UpdateCommandState();
        try
        {
            var clear = await _controller.ClearAsync(_lifetimeCancellation.Token);
            if (!CanUpdateUi())
            {
                return;
            }

            var statusMessage = DescribeClearStatus(clear);
            if (clear.Succeeded)
            {
                await RefreshAsync();
                if (CanUpdateUi())
                {
                    HistoryStatusTextBlock.Text = statusMessage;
                }
            }
            else
            {
                HistoryStatusTextBlock.Text = statusMessage;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (CanUpdateUi())
            {
                HistoryStatusTextBlock.Text = AppStrings.Get("history.status.clear_failed");
            }
        }
        finally
        {
            _isClearing = false;
            if (CanUpdateUi())
            {
                UpdateCommandState();
            }
        }
    }

    private void CloseHistoryButtonClick(object sender, RoutedEventArgs eventArgs) => Close();

    private async Task RefreshAsync()
    {
        var generation = ++_loadGeneration;
        _isLoading = true;
        UpdateCommandState();
        try
        {
            var loaded = await _controller.LoadAsync(_lifetimeCancellation.Token);
            if (!IsCurrentLoad(generation))
            {
                return;
            }

            var items = loaded.Entries.Select(entry => new HistoryListItem(entry, DescribeListItem(entry))).ToArray();
            HistoryListBox.ItemsSource = items;
            if (items.Length > 0)
            {
                HistoryListBox.SelectedIndex = 0;
            }
            else
            {
                DisplayEntry(null);
            }

            HistoryStatusTextBlock.Text = DescribeLoadStatus(loaded);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (IsCurrentLoad(generation))
            {
                HistoryListBox.ItemsSource = Array.Empty<HistoryListItem>();
                DisplayEntry(null);
                HistoryStatusTextBlock.Text = AppStrings.Get("history.status.load_failed");
            }
        }
        finally
        {
            if (IsCurrentLoad(generation))
            {
                _isLoading = false;
                UpdateCommandState();
            }
        }
    }

    private void DisplayEntry(HistoryListItem? item)
    {
        HistorySourceTextBox.Text = item?.Entry.Request.Text ?? string.Empty;
        HistoryResultTextBox.Text = item is null ? string.Empty : DescribeResult(item.Entry.Result);
    }

    private void HandleClosed(object? sender, EventArgs eventArgs)
    {
        _isClosed = true;
        ++_loadGeneration;
        _lifetimeCancellation.Cancel();
    }

    private bool CanUpdateUi() => !_isClosed && !_lifetimeCancellation.IsCancellationRequested;

    private bool IsCurrentLoad(int generation) => CanUpdateUi() && _loadGeneration == generation;

    private void UpdateCommandState()
    {
        var isEnabled = CanUpdateUi() && !_isLoading && !_isClearing;
        RefreshHistoryButton.IsEnabled = isEnabled;
        ClearHistoryButton.IsEnabled = isEnabled;
    }

    private static string DescribeLoadStatus(HistoryLoadResult loaded)
    {
        if (loaded.ConfigurationStatus is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound))
        {
            return loaded.ConfigurationStatus == PersistenceStatus.Cancelled
                ? AppStrings.Get("history.status.configuration_cancelled")
                : AppStrings.Get("history.status.configuration_failed");
        }

        return loaded.HistoryStatus switch
        {
            PersistenceStatus.Succeeded when loaded.CorruptLineCount > 0 =>
                AppStrings.Format("history.status.loaded_corrupt", loaded.Entries.Count, loaded.CorruptLineCount),
            PersistenceStatus.Succeeded when loaded.Entries.Count == 0 => AppStrings.Get("history.status.empty"),
            PersistenceStatus.Succeeded => AppStrings.Format("history.status.loaded", loaded.Entries.Count),
            PersistenceStatus.NotFound => AppStrings.Get("history.status.empty"),
            PersistenceStatus.Cancelled => AppStrings.Get("history.status.read_cancelled"),
            _ => AppStrings.Get("history.status.read_failed"),
        };
    }

    private static string DescribeClearStatus(HistoryClearResult clear) => clear.Status switch
    {
        PersistenceStatus.Succeeded => AppStrings.Get("history.status.cleared"),
        PersistenceStatus.NotFound => AppStrings.Get("history.status.already_empty"),
        PersistenceStatus.Cancelled => AppStrings.Get("history.status.clear_cancelled"),
        _ => AppStrings.Get("history.status.clear_unavailable"),
    };

    private static string DescribeListItem(HistoryEntry entry) => AppStrings.Format(
        "history.list.item",
        entry.CreatedAt.LocalDateTime,
        DescribeProvider(entry.Request.Provider.ProviderId),
        DescribeTerminalState(entry.Result.TerminalState),
        DescribeQueryKind(entry.Request.QueryKind),
        Summarize(entry.Request.Text));

    private static string DescribeProvider(string providerId) => LocalDictionaryIds.IsFile(providerId)
        ? AppStrings.Get("result.source.local_dictionary")
        : providerId;

    private static string DescribeResult(QueryResult result) => result.TerminalState switch
    {
        QueryTerminalState.Completed => result.Result?.Text ?? AppStrings.Get("history.result.unavailable"),
        QueryTerminalState.Cancelled => AppStrings.Get("history.result.cancelled"),
        QueryTerminalState.Failed => DescribeFailure(result.Error?.Code ?? QueryErrorCode.Internal),
        _ => AppStrings.Get("history.result.state_unavailable"),
    };

    private static string DescribeFailure(QueryErrorCode errorCode) => AppStrings.Format(
        "history.result.failed",
        AppStrings.DescribeQueryErrorCode(errorCode),
        AppStrings.DescribeQueryError(errorCode));

    private static string DescribeQueryKind(QueryKind queryKind) => queryKind switch
    {
        QueryKind.Translation => AppStrings.Get("history.query_kind.translation"),
        QueryKind.Dictionary => AppStrings.Get("history.query_kind.dictionary"),
        QueryKind.Ocr => AppStrings.Get("history.query_kind.ocr"),
        _ => AppStrings.Get("history.query_kind.translation"),
    };

    private static string DescribeTerminalState(QueryTerminalState terminalState) => terminalState switch
    {
        QueryTerminalState.Completed => AppStrings.Get("history.terminal.completed"),
        QueryTerminalState.Cancelled => AppStrings.Get("history.terminal.cancelled"),
        QueryTerminalState.Failed => AppStrings.Get("history.terminal.failed"),
        _ => AppStrings.Get("history.terminal.failed"),
    };

    private static string Summarize(string text)
    {
        var singleLine = text.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= 72 ? singleLine : singleLine[..72] + "…";
    }

    private sealed record HistoryListItem(HistoryEntry Entry, string Label);
}
