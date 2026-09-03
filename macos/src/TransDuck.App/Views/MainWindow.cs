using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using TransDuck.Core;
using TransDuck.UI.Views;

namespace TransDuck.MacOS.App.Views;

internal sealed class MainWindow : TranslationWindowBase
{
    private readonly MacAppRuntime _runtime;
    private long _lastAppliedRevision = -1;
    private bool _allowClose;

    public MainWindow(MacAppRuntime runtime)
    {
        _runtime = runtime;
        ConfigureForMacDesktopWindow();
        VersionTextBlock.Text = ProductVersionDisplay.FromAssembly(typeof(App).Assembly);
        TranslationRequested += HandleTranslationRequested;
        SelectedTextRequested += HandleSelectedTextRequested;
        CaptureOcrRequested += HandleCaptureOcrRequested;
        CancellationRequested += HandleCancellationRequested;
        RetryRequested += HandleRetryRequested;
        PronunciationRequested += HandlePronunciationRequested;
        ResultCopyRequested += HandleResultCopyRequested;
        SettingsRequested += HandleSettingsRequested;
        HistoryRequested += HandleHistoryRequested;
        _runtime.StateChanged += HandleRuntimeStateChanged;
        Closing += HandleClosing;
        ApplyState(_runtime.State);
    }

    internal void PrepareForShutdown()
    {
        _allowClose = true;
        _runtime.StateChanged -= HandleRuntimeStateChanged;
    }

    private void HandleRuntimeStateChanged(object? sender, MacRuntimeState state) =>
        Dispatcher.UIThread.Post(() => ApplyState(state));

    private void ApplyState(MacRuntimeState state)
    {
        if (state.Revision < _lastAppliedRevision)
        {
            return;
        }

        _lastAppliedRevision = state.Revision;
        if (!string.Equals(InputTextBox.Text, state.Input, StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(state.Input))
        {
            InputTextBox.Text = state.Input;
        }

        ResultsItemsControl.ItemsSource = state.Results;
        StatusTextBlock.Text = state.Status;
        TranslateButton.IsEnabled = !state.IsBusy;
        SelectedTextButton.IsEnabled = !state.IsBusy;
        OcrButton.IsEnabled = !state.IsBusy;
        CancelButton.IsEnabled = state.IsBusy;
        RetryButton.IsEnabled = !state.IsBusy && state.CanRetry;
        CopyButton.IsEnabled = state.Results.Any(static result => !string.IsNullOrWhiteSpace(result.Text));
    }

    private void HandleTranslationRequested(object? sender, string text) =>
        _ = _runtime.TranslateAsync(text);

    private void HandleSelectedTextRequested(object? sender, EventArgs eventArgs) =>
        _ = _runtime.TranslateSelectedTextAsync(promptForPermission: true);

    private void HandleCaptureOcrRequested(object? sender, string language) =>
        _ = _runtime.CaptureOcrAndTranslateAsync(language);

    private void HandleCancellationRequested(object? sender, EventArgs eventArgs) =>
        _runtime.CancelCurrentOperation();

    private void HandleRetryRequested(object? sender, EventArgs eventArgs) => _ = _runtime.RetryAsync();

    private void HandlePronunciationRequested(object? sender, string term) =>
        _ = _runtime.PronounceAsync(term);

    private async void HandleResultCopyRequested(object? sender, string output)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is null || string.IsNullOrEmpty(output))
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(output);
            StatusTextBlock.Text = "Result copied.";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            StatusTextBlock.Text = "The result could not be copied.";
        }
    }

    private void HandleSettingsRequested(object? sender, EventArgs eventArgs) =>
        (Application.Current as App)?.ShowSettingsWindow();

    private void HandleHistoryRequested(object? sender, EventArgs eventArgs) =>
        (Application.Current as App)?.ShowHistoryWindow();

    private void HandleClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (!_allowClose &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            eventArgs.Cancel = true;
            _runtime.StopPronunciation();
            Hide();
        }
    }
}
