using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace TransDuck.UI.Views;

public partial class TranslationWindowBase : Window
{
    public TranslationWindowBase()
    {
        InitializeComponent();
        HeaderPanelElement.PointerPressed += HandleHeaderPointerPressed;
    }

    public event EventHandler<string>? TranslationRequested;
    public event EventHandler? SelectedTextRequested;
    public event EventHandler<string>? CaptureOcrRequested;
    public event EventHandler? CancellationRequested;
    public event EventHandler<string>? ResultCopyRequested;
    public event EventHandler? RetryRequested;
    public event EventHandler<string>? PronunciationRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? HideRequested;
    public event EventHandler<PointerPressedEventArgs>? HeaderPointerPressed;

    protected TextBox InputTextBox => InputTextBoxElement;
    protected ItemsControl ResultItemsControl => ResultsItemsControlElement;
    protected ItemsControl ResultsItemsControl => ResultsItemsControlElement;
    protected TextBlock StatusTextBlock => StatusTextBlockElement;
    protected TextBlock SelectionHintTextBlock => SelectionHintTextBlockElement;
    protected TextBlock TranslationErrorCodeTextBlock => TranslationErrorCodeTextBlockElement;
    protected TextBlock ProductVersionTextBlock => VersionTextBlockElement;
    protected TextBlock VersionTextBlock => VersionTextBlockElement;
    protected Button TranslateButton => TranslateButtonElement;
    protected Button SelectedTextButton => SelectedTextButtonElement;
    protected Button CaptureOcrButton => CaptureOcrButtonElement;
    protected Button OcrButton => CaptureOcrButtonElement;
    protected Button CancelButton => CancelButtonElement;
    protected Button RetryButton => RetryButtonElement;
    protected Button CopyResultButton => CopyResultButtonElement;
    protected Button CopyButton => CopyResultButtonElement;
    protected ComboBox OcrLanguageBox => OcrLanguageComboBoxElement;
    protected ComboBox OcrLanguageComboBox => OcrLanguageComboBoxElement;

    protected void ConfigureForWindowsFloatingWindow()
    {
        Width = 560;
        Height = 560;
        MinWidth = 420;
        MinHeight = 360;
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        WindowStartupLocation = WindowStartupLocation.Manual;
        CanResize = true;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        RootBorderElement.Bind(Border.BackgroundProperty, new DynamicResourceExtension("SystemRegionBrush"));
        RootBorderElement.BorderBrush = new SolidColorBrush(Color.Parse("#2A6D9D"));
        HistoryButtonElement.IsVisible = false;
        SettingsButtonElement.IsVisible = false;
        SelectedTextButtonElement.IsVisible = false;
        HideButtonElement.IsVisible = true;
        SelectionHintTextBlockElement.IsVisible = true;
    }

    protected void ConfigureForMacDesktopWindow()
    {
        Width = 760;
        Height = 650;
        MinWidth = 560;
        MinHeight = 480;
        RootBorderElement.Margin = new Thickness(0);
        RootBorderElement.Padding = new Thickness(20);
        RootBorderElement.CornerRadius = new CornerRadius(0);
        RootBorderElement.BorderThickness = new Thickness(0);
        HistoryButtonElement.IsVisible = true;
        SettingsButtonElement.IsVisible = true;
        SelectedTextButtonElement.IsVisible = true;
        HideButtonElement.IsVisible = false;
        SelectionHintTextBlockElement.IsVisible = false;
    }

    protected string CombinedResult() => string.Join(
        Environment.NewLine + Environment.NewLine,
        (ResultsItemsControlElement.ItemsSource ?? Enumerable.Empty<object>())
            .OfType<TranslationResultViewModel>()
            .Where(static result => !string.IsNullOrWhiteSpace(result.Text))
            .Select(static result => result.DisplayName + Environment.NewLine + result.Text));

    private void HandleTranslateClick(object? sender, RoutedEventArgs eventArgs) =>
        TranslationRequested?.Invoke(this, InputTextBoxElement.Text ?? string.Empty);

    private void HandleSelectedTextClick(object? sender, RoutedEventArgs eventArgs) =>
        SelectedTextRequested?.Invoke(this, EventArgs.Empty);

    private void HandleCaptureOcrClick(object? sender, RoutedEventArgs eventArgs)
    {
        var language = (OcrLanguageComboBoxElement.SelectedItem as ComboBoxItem)?.Tag as string ?? "en-US";
        CaptureOcrRequested?.Invoke(this, language);
    }

    private void HandleCancelClick(object? sender, RoutedEventArgs eventArgs) =>
        CancellationRequested?.Invoke(this, EventArgs.Empty);

    private void HandleRetryClick(object? sender, RoutedEventArgs eventArgs) =>
        RetryRequested?.Invoke(this, EventArgs.Empty);

    private void HandleCopyClick(object? sender, RoutedEventArgs eventArgs) =>
        ResultCopyRequested?.Invoke(this, CombinedResult());

    private void HandlePronounceClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Control { Tag: string term } && !string.IsNullOrWhiteSpace(term))
        {
            PronunciationRequested?.Invoke(this, term);
        }
    }

    private void HandleHistoryClick(object? sender, RoutedEventArgs eventArgs) =>
        HistoryRequested?.Invoke(this, EventArgs.Empty);

    private void HandleSettingsClick(object? sender, RoutedEventArgs eventArgs) =>
        SettingsRequested?.Invoke(this, EventArgs.Empty);

    private void HandleHideClick(object? sender, RoutedEventArgs eventArgs) =>
        HideRequested?.Invoke(this, EventArgs.Empty);

    private void HandleHeaderPointerPressed(object? sender, PointerPressedEventArgs eventArgs) =>
        HeaderPointerPressed?.Invoke(this, eventArgs);
}
