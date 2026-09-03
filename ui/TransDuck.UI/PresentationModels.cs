using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TransDuck.UI;

public sealed record QuerySourcePresentation(string Key, string DisplayName);

public sealed record HistoryItemViewModel(string Label, string SourceText, string ResultText);

public sealed class TranslationResultViewModel : INotifyPropertyChanged
{
    private string _displayName;
    private string _text;
    private string _status;
    private string? _pronunciationTerm;

    public TranslationResultViewModel(
        string key,
        string displayName,
        string text,
        string status,
        string? pronunciationTerm = null)
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanPronounce)));
        }
    }

    public bool CanPronounce => !string.IsNullOrWhiteSpace(PronunciationTerm);

    public TranslationResultViewModel WithStatus(string status) => new(
        Key,
        DisplayName,
        Text,
        status,
        PronunciationTerm);

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
