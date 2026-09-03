// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.UI;

namespace TransDuck.App.Services;

/// <summary>
/// Selects the Avalonia resource overlay before windows are created and resolves complete localized messages.
/// </summary>
internal static class AppStrings
{
    public static void InitializeForCurrentCulture() => UiStrings.InitializeForCurrentCulture();

    public static string Get(string key) => UiStrings.Get(key);

    public static string Format(string key, params object?[] arguments) =>
        UiStrings.Format(key, arguments);

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
