using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace TransDuck.MacOS.App.Views;

internal partial class MainWindow : Window
{
    private readonly MacAppRuntime _runtime;
    private long _lastAppliedRevision = -1;
    private bool _allowClose;

    public MainWindow(MacAppRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
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

    private void HandleTranslateClick(object? sender, RoutedEventArgs eventArgs) =>
        _ = _runtime.TranslateAsync(InputTextBox.Text ?? string.Empty);

    private void HandleSelectedTextClick(object? sender, RoutedEventArgs eventArgs) =>
        _ = _runtime.TranslateSelectedTextAsync(promptForPermission: true);

    private void HandleOcrClick(object? sender, RoutedEventArgs eventArgs)
    {
        var language = (OcrLanguageComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "en-US";
        _ = _runtime.CaptureOcrAndTranslateAsync(language);
    }

    private void HandleCancelClick(object? sender, RoutedEventArgs eventArgs) =>
        _runtime.CancelCurrentOperation();

    private void HandleRetryClick(object? sender, RoutedEventArgs eventArgs) => _ = _runtime.RetryAsync();

    private async void HandleCopyClick(object? sender, RoutedEventArgs eventArgs)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        var output = _runtime.State.Output;
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

    private void HandleSettingsClick(object? sender, RoutedEventArgs eventArgs) =>
        (Application.Current as App)?.ShowSettingsWindow();

    private void HandleHistoryClick(object? sender, RoutedEventArgs eventArgs) =>
        (Application.Current as App)?.ShowHistoryWindow();

    private void HandleClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (!_allowClose &&
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            eventArgs.Cancel = true;
            Hide();
        }
    }
}
