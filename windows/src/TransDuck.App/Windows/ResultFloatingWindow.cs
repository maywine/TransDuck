using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using TransDuck.App.Services;
using TransDuck.UI;
using TransDuck.UI.Views;

namespace TransDuck.App.Windows;

/// <summary>
/// Presents source text, streaming output, selection lookup and OCR commands in a borderless floating window.
/// </summary>
public sealed class ResultFloatingWindow : TranslationWindowBase
{
    private bool _allowClose;
    private readonly ObservableCollection<TranslationResultViewModel> _results = [];

    public ResultFloatingWindow()
    {
        ConfigureForWindowsFloatingWindow();
        ProductVersionTextBlock.Text = TransDuck.Core.ProductVersionDisplay.FromAssembly(typeof(App).Assembly);
        ResultItemsControl.ItemsSource = _results;
        StatusTextBlock.Text = AppStrings.Get("result.hint.default");
        Deactivated += HandleDeactivated;
        HideRequested += HandleHideRequested;
        HeaderPointerPressed += HandleHeaderPointerPressed;
    }

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
                _results.Add(new TranslationResultViewModel(
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
            result = new TranslationResultViewModel(key, displayName, text, status, pronunciationTerm);
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
        TranslationErrorCodeTextBlock.IsVisible = true;
    }

    public void ClearTranslationErrorCode()
    {
        TranslationErrorCodeTextBlock.Text = string.Empty;
        TranslationErrorCodeTextBlock.IsVisible = false;
    }

    public void ClearResult() => _results.Clear();

    public void AllowFinalClose() => _allowClose = true;

    protected override void OnClosing(WindowClosingEventArgs eventArgs)
    {
        if (!_allowClose)
        {
            eventArgs.Cancel = true;
            PronunciationStopRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        }

        base.OnClosing(eventArgs);
    }

    private void HandleDeactivated(object? sender, EventArgs eventArgs)
    {
        if (!_allowClose && IsVisible)
        {
            PronunciationStopRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        }
    }

    private void HandleHideRequested(object? sender, EventArgs eventArgs)
    {
        PronunciationStopRequested?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void HandleHeaderPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(eventArgs);
        }
    }

}
