using System.Globalization;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace TransDuck.UI;

public static class UiStrings
{
    private static readonly Uri ResourceBaseUri = new("avares://TransDuck.UI/");
    private static readonly Uri EnglishDictionaryUri =
        new("avares://TransDuck.UI/Resources/Strings.en-US.axaml");
    private static readonly Uri ChineseDictionaryUri =
        new("avares://TransDuck.UI/Resources/Strings.zh-CN.axaml");
    private static ResourceInclude? _englishDictionary;
    private static ResourceInclude? _localizedDictionary;

    public static void InitializeForCurrentCulture()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        Remove(dictionaries, ref _localizedDictionary);
        Remove(dictionaries, ref _englishDictionary);
        _englishDictionary = Add(dictionaries, EnglishDictionaryUri);
        if (CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            _localizedDictionary = Add(dictionaries, ChineseDictionaryUri);
        }
    }

    public static string Get(string key)
    {
        var value = Application.Current?.TryGetResource(key, theme: null, out var resource) == true
            ? resource as string
            : null;
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    private static ResourceInclude Add(
        IList<Avalonia.Controls.IResourceProvider> dictionaries,
        Uri source)
    {
        var dictionary = new ResourceInclude(ResourceBaseUri) { Source = source };
        dictionaries.Add(dictionary);
        return dictionary;
    }

    private static void Remove(
        IList<Avalonia.Controls.IResourceProvider> dictionaries,
        ref ResourceInclude? dictionary)
    {
        if (dictionary is { } current)
        {
            dictionaries.Remove(current);
            dictionary = null;
        }
    }
}
