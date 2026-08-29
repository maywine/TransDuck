// Copyright (c) 2026 maywine. All rights reserved.

using System.Globalization;
using System.Windows;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.App.Services;

/// <summary>
/// Selects the WPF resource overlay before windows are created and resolves complete localized messages.
/// </summary>
internal static class AppStrings
{
    private const string ChineseDictionaryPath = "Resources/Strings.zh-CN.xaml";

    public static void InitializeForCurrentCulture()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        var dictionaries = application.Resources.MergedDictionaries;
        foreach (var dictionary in dictionaries
                     .Where(dictionary => dictionary.Source?.OriginalString.EndsWith(
                         ChineseDictionaryPath,
                         StringComparison.OrdinalIgnoreCase) == true)
                     .ToArray())
        {
            dictionaries.Remove(dictionary);
        }

        if (!CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(ChineseDictionaryPath, UriKind.Relative),
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // App.xaml keeps en-US merged first, so a failed overlay cannot produce blank UI.
        }
    }

    public static string Get(string key)
    {
        var value = Application.Current?.TryFindResource(key) as string;
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentUICulture, Get(key), arguments);

    public static string DescribeQueryError(QueryErrorCode errorCode) => errorCode switch
    {
        QueryErrorCode.Authentication => Get("translation.error.authentication"),
        QueryErrorCode.RateLimited => Get("translation.error.rate_limited"),
        QueryErrorCode.Timeout => Get("translation.error.timeout"),
        QueryErrorCode.Network => Get("translation.error.network"),
        QueryErrorCode.ProviderUnavailable => Get("translation.error.provider_unavailable"),
        QueryErrorCode.InvalidRequest => Get("translation.error.invalid_request"),
        QueryErrorCode.UnsupportedLanguage => Get("translation.error.unsupported_language"),
        _ => Get("translation.error.internal"),
    };

    public static string DescribeQueryErrorCode(QueryErrorCode errorCode) => errorCode switch
    {
        QueryErrorCode.Authentication => Get("translation.error_code.authentication"),
        QueryErrorCode.RateLimited => Get("translation.error_code.rate_limited"),
        QueryErrorCode.Timeout => Get("translation.error_code.timeout"),
        QueryErrorCode.Network => Get("translation.error_code.network"),
        QueryErrorCode.ProviderUnavailable => Get("translation.error_code.provider_unavailable"),
        QueryErrorCode.InvalidRequest => Get("translation.error_code.invalid_request"),
        QueryErrorCode.UnsupportedLanguage => Get("translation.error_code.unsupported_language"),
        _ => Get("translation.error_code.internal"),
    };
}
