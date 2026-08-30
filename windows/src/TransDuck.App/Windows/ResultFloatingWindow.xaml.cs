using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
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
    private readonly ObservableCollection<QuerySourceResultViewModel> _results = [];

    public ResultFloatingWindow()
    {
        InitializeComponent();
        ProductVersionTextBlock.Text = TransDuck.Core.ProductVersionDisplay.FromAssembly(typeof(App).Assembly);
        ResultItemsControl.ItemsSource = _results;
        StatusTextBlock.Text = AppStrings.Get("result.hint.default");
    }

    public event EventHandler<string>? TranslationRequested;

    public event EventHandler<string>? CaptureOcrRequested;

    public event EventHandler? CancellationRequested;

    public event EventHandler<string>? ResultCopyRequested;

    public event EventHandler? RetryRequested;

    public event EventHandler<string>? PronunciationRequested;

    public event EventHandler? PronunciationStopRequested;

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

    public void SetResult(string text) => SetSourceResult(
        "result",
        AppStrings.Get("result.source.result"),
        text,
        string.Empty);

    public void BeginResults(
        IEnumerable<QuerySourcePresentation> sources,
        bool preserveExisting = false)
    {
        if (!preserveExisting)
        {
            _results.Clear();
        }

        foreach (var source in sources)
        {
            var existing = _results.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, source.Key, StringComparison.Ordinal));
            if (existing is null)
            {
                _results.Add(new QuerySourceResultViewModel(
                    source.Key,
                    source.DisplayName,
                    string.Empty,
                    AppStrings.Get("result.source.waiting"),
                    pronunciationTerm: null));
            }
            else
            {
                existing.DisplayName = source.DisplayName;
                existing.Text = string.Empty;
                existing.Status = AppStrings.Get("result.source.waiting");
                existing.PronunciationTerm = null;
            }
        }
    }

    public void SetSourceResult(
        string key,
        string displayName,
        string text,
        string status,
        string? pronunciationTerm = null)
    {
        var result = _results.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (result is null)
        {
            result = new QuerySourceResultViewModel(key, displayName, text, status, pronunciationTerm);
            _results.Add(result);
            return;
        }

        result.DisplayName = displayName;
        result.Text = text;
        result.Status = status;
        result.PronunciationTerm = pronunciationTerm;
    }

    public void SetSourceStatus(string key, string status)
    {
        var result = _results.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (result is not null)
        {
            result.Status = status;
        }
    }

    public void MarkActiveSourcesCancelled()
    {
        var waiting = AppStrings.Get("result.source.waiting");
        var receiving = AppStrings.Get("result.source.receiving");
        var cancelled = AppStrings.Get("result.source.cancelled");
        foreach (var result in _results.Where(result =>
                     string.Equals(result.Status, waiting, StringComparison.Ordinal) ||
                     string.Equals(result.Status, receiving, StringComparison.Ordinal)))
        {
            result.Status = cancelled;
        }
    }

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

    public void ClearResult() => _results.Clear();

    public void AllowFinalClose() => _allowClose = true;

    protected override void OnClosing(CancelEventArgs eventArgs)
    {
        if (!_allowClose)
        {
            eventArgs.Cancel = true;
            PronunciationStopRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        }

        base.OnClosing(eventArgs);
    }

    protected override void OnDeactivated(EventArgs eventArgs)
    {
        base.OnDeactivated(eventArgs);
        if (!_allowClose && IsVisible)
        {
            PronunciationStopRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        }
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
        ResultCopyRequested?.Invoke(this, GetCombinedResult());

    private void RetryButtonClick(object sender, RoutedEventArgs eventArgs) =>
        RetryRequested?.Invoke(this, EventArgs.Empty);

    private void PronounceButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is FrameworkElement { Tag: string term } && !string.IsNullOrWhiteSpace(term))
        {
            PronunciationRequested?.Invoke(this, term);
        }
    }

    private void HideButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        PronunciationStopRequested?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void TitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (eventArgs.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private string GetCombinedResult() => string.Join(
        Environment.NewLine + Environment.NewLine,
        _results
            .Where(static result => !string.IsNullOrWhiteSpace(result.Text))
            .Select(static result => $"{result.DisplayName}{Environment.NewLine}{result.Text}"));
}

public sealed record QuerySourcePresentation(string Key, string DisplayName);

public sealed class QuerySourceResultViewModel : INotifyPropertyChanged
{
    private string _displayName;
    private string _text;
    private string _status;
    private string? _pronunciationTerm;

    public QuerySourceResultViewModel(
        string key,
        string displayName,
        string text,
        string status,
        string? pronunciationTerm)
    {
        Key = key;
        _displayName = displayName;
        _text = text;
        _status = status;
        _pronunciationTerm = pronunciationTerm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string Text
    {
        get => _text;
        set => SetField(ref _text, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string? PronunciationTerm
    {
        get => _pronunciationTerm;
        set
        {
            if (string.Equals(_pronunciationTerm, value, StringComparison.Ordinal))
            {
                return;
            }

            _pronunciationTerm = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PronunciationTerm)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PronunciationVisibility)));
        }
    }

    public Visibility PronunciationVisibility => string.IsNullOrWhiteSpace(PronunciationTerm)
        ? Visibility.Collapsed
        : Visibility.Visible;

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (string.Equals(field, value, StringComparison.Ordinal))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
