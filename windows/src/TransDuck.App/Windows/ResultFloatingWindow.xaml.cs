using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TransDuck.App.Services;

namespace TransDuck.App.Windows;

/// <summary>
/// Presents source text, streaming output, selection lookup and OCR commands in a borderless floating window.
/// </summary>
public partial class ResultFloatingWindow : Window
{
    private bool _allowClose;

    public ResultFloatingWindow()
    {
        InitializeComponent();
        StatusTextBlock.Text = AppStrings.Get("result.hint.default");
    }

    public event EventHandler<string>? TranslationRequested;

    public event EventHandler<string>? CaptureOcrRequested;

    public event EventHandler? CancellationRequested;

    public event EventHandler<string>? ResultCopyRequested;

    public event EventHandler? RetryRequested;

    public void Present(string? text = null)
    {
        if (text is not null)
        {
            InputTextBox.Text = text;
        }

        if (!IsVisible)
        {
            Show();
        }

        Activate();
        InputTextBox.Focus();
    }

    public void SetResult(string text) => ResultTextBox.Text = text;

    public void SetStatus(string text) => StatusTextBlock.Text = text;

    public void SetSelectionHotkeyHint(string? hotkeyText) =>
        SelectionHintTextBlock.Text = hotkeyText is null
            ? AppStrings.Get("hotkey.unavailable")
            : AppStrings.Format("result.hint.active", hotkeyText);

    public void SetRetryEnabled(bool isEnabled) => RetryButton.IsEnabled = isEnabled;

    public void ShowTranslationErrorCode(string errorCode)
    {
        TranslationErrorCodeTextBlock.Text = AppStrings.Format("result.error_code.label", errorCode);
        TranslationErrorCodeTextBlock.Visibility = Visibility.Visible;
    }

    public void ClearTranslationErrorCode()
    {
        TranslationErrorCodeTextBlock.Text = string.Empty;
        TranslationErrorCodeTextBlock.Visibility = Visibility.Collapsed;
    }

    public void ClearResult() => ResultTextBox.Clear();

    public void AllowFinalClose() => _allowClose = true;

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (!_allowClose)
        {
            eventArgs.Cancel = true;
            Hide();
        }

        base.OnClosing(eventArgs);
    }

    private void TranslateButtonClick(object sender, RoutedEventArgs eventArgs) =>
        TranslationRequested?.Invoke(this, InputTextBox.Text);

    private void CaptureOcrButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        var language = (OcrLanguageBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "en-US";
        CaptureOcrRequested?.Invoke(this, language);
    }

    private void CancelButtonClick(object sender, RoutedEventArgs eventArgs) =>
        CancellationRequested?.Invoke(this, EventArgs.Empty);

    private void CopyResultButtonClick(object sender, RoutedEventArgs eventArgs) =>
        ResultCopyRequested?.Invoke(this, ResultTextBox.Text);

    private void RetryButtonClick(object sender, RoutedEventArgs eventArgs) =>
        RetryRequested?.Invoke(this, EventArgs.Empty);

    private void HideButtonClick(object sender, RoutedEventArgs eventArgs) => Hide();

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
