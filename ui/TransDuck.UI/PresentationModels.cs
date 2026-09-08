using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TransDuck.UI;

public sealed record QuerySourcePresentation(string Key, string DisplayName, string? TargetLanguage = null);

public sealed record HistoryItemViewModel(string Label, string SourceText, string ResultText);

public sealed class TranslationResultViewModel : INotifyPropertyChanged
{
    private string _displayName;
    private string _text;
    private string _status;
    private string? _pronunciationTerm;
    private string? _targetLanguage;

    public TranslationResultViewModel(
        string key,
        string displayName,
        string text,
        string status,
        string? pronunciationTerm = null,
        string? targetLanguage = null)
    {
        Key = key;
        _targetLanguage = targetLanguage;
        _displayName = displayName;
        _text = text;
        _status = status;
        _pronunciationTerm = pronunciationTerm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key { get; }
    public string? TargetLanguage
    {
        get => _targetLanguage;
        set
        {
            if (_targetLanguage == value) return;
            _targetLanguage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetLanguage)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TargetLanguageLabel)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTargetLanguage)));
        }
    }
    public bool HasTargetLanguage => !string.IsNullOrWhiteSpace(TargetLanguage);
    public string TargetLanguageLabel => HasTargetLanguage
        ? UiStrings.Format("result.target.label", LanguageDisplayName(TargetLanguage!))
        : string.Empty;
    public bool CanCopy => !string.IsNullOrWhiteSpace(Text);

    private static string LanguageDisplayName(string language) => language.ToLowerInvariant() switch
    {
        "zh" or "zh-hans" or "zh-cn" => UiStrings.Get("result.language.chinese_simplified"),
        "zh-hant" or "zh-tw" => UiStrings.Get("result.language.chinese_traditional"),
        "en" or "en-us" or "en-gb" => UiStrings.Get("result.language.english"),
        "ja" => UiStrings.Get("result.language.japanese"),
        "ko" => UiStrings.Get("result.language.korean"),
        _ => language,
    };

    public string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }

    public string Text
    {
        get => _text;
        set
        {
            SetField(ref _text, value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCopy)));
        }
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
        PronunciationTerm,
        TargetLanguage);

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
