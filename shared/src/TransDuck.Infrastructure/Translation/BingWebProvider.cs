// Copyright (c) 2026 maywine. All rights reserved.

using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Infrastructure.Translation;

/// <summary>
/// Adapts the unofficial Bing Translator web protocol without persisting request tokens or cookies.
/// </summary>
public sealed partial class BingWebProvider : ITranslationProvider
{
    public const string DefaultEndpoint = "https://cn.bing.com/translator";

    private const int MaxCookieLength = 8192;
    private const int MaxIdentifierLength = 512;
    private const int MaxTokenLength = 4096;
    private readonly ITranslationHttpClientLeaseSource _clientLeaseSource;

    /// <summary>Creates a provider using an externally owned HttpClient.</summary>
    public BingWebProvider(HttpClient httpClient)
        : this(new FixedTranslationHttpClientLeaseSource(httpClient))
    {
    }

    /// <summary>Creates a provider using one application-owned transport lease source.</summary>
    public BingWebProvider(ITranslationHttpClientLeaseSource clientLeaseSource)
    {
        _clientLeaseSource = clientLeaseSource ?? throw new ArgumentNullException(nameof(clientLeaseSource));
    }

    /// <inheritdoc />
    public ProviderRegistration Registration { get; } = new(
        new ProviderDescriptor(TranslationProviderIds.Bing),
        ProviderCapability.Translation);

    /// <inheritdoc />
    public IAsyncEnumerable<TranslationStreamEvent> TranslateAsync(
        TranslationProviderRequest request,
        CancellationToken cancellationToken) =>
        StreamAsync(request, cancellationToken);

    private async IAsyncEnumerable<TranslationStreamEvent> StreamAsync(
        TranslationProviderRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!TryValidateRequest(request))
        {
            yield return TranslationProviderFailures.InvalidRequest();
            yield break;
        }

        var cookie = request.Credentials.GetApiKey();
        if (!IsValidCookie(cookie))
        {
            yield return TranslationProviderFailures.InvalidRequest();
            yield break;
        }

        using var clientLease = TranslationHttpClientLeases.TryAcquire(_clientLeaseSource, request.Endpoint);
        if (clientLease is null)
        {
            yield return TranslationProviderFailures.ProviderUnavailable();
            yield break;
        }

        using var timeoutSource = new CancellationTokenSource(request.Timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var tokenResult = await FetchTokenAsync(
                clientLease.Client,
                request.Endpoint,
                cookie,
                linkedSource.Token,
                cancellationToken,
                timeoutSource.Token).ConfigureAwait(false);
            if (tokenResult.Failure is not null)
            {
                yield return tokenResult.Failure;
                yield break;
            }

            using var message = CreateTranslationRequest(request, tokenResult.Token!, cookie);
            var response = await WebTranslationHttp.SendAsync(
                clientLease.Client,
                message,
                linkedSource.Token,
                cancellationToken,
                timeoutSource.Token).ConfigureAwait(false);
            if (response.Failure is not null)
            {
                yield return response.Failure;
                yield break;
            }

            if (response.StatusCode == HttpStatusCode.ResetContent || IsTokenInvalid(response.Payload!))
            {
                if (attempt == 0)
                {
                    continue;
                }

                yield return TranslationProviderFailures.ProviderUnavailable();
                yield break;
            }

            var text = TryReadTranslation(response.Payload!);
            if (string.IsNullOrEmpty(text))
            {
                yield return TranslationProviderFailures.Internal();
                yield break;
            }

            yield return TranslationStreamEvent.Delta(text);
            yield return TranslationStreamEvent.Completed();
            yield break;
        }
    }

    private async Task<BingTokenResult> FetchTokenAsync(
        HttpClient httpClient,
        Uri endpoint,
        string? cookie,
        CancellationToken requestToken,
        CancellationToken callerToken,
        CancellationToken timeoutToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
        AddCookie(message, cookie);
        var response = await WebTranslationHttp.SendAsync(
            httpClient,
            message,
            requestToken,
            callerToken,
            timeoutToken).ConfigureAwait(false);
        if (response.Failure is not null)
        {
            return BingTokenResult.Failed(response.Failure);
        }

        return TryReadToken(response.Payload!, out var token)
            ? BingTokenResult.Succeeded(token)
            : BingTokenResult.Failed(TranslationProviderFailures.Internal());
    }

    private static bool TryValidateRequest(TranslationProviderRequest? request)
    {
        if (request is null || string.Equals(request.TargetLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            request.ValidateForProvider(TranslationProviderIds.Bing, modelRequired: false);
            return string.IsNullOrEmpty(request.Endpoint.UserInfo) &&
                string.IsNullOrEmpty(request.Endpoint.Query) &&
                string.IsNullOrEmpty(request.Endpoint.Fragment);
        }
        catch (Exception exception) when (exception is ArgumentException or ContractValidationException)
        {
            return false;
        }
    }

    private static HttpRequestMessage CreateTranslationRequest(
        TranslationProviderRequest request,
        BingWebToken token,
        string? cookie)
    {
        var fields = new[]
        {
            new KeyValuePair<string, string>("text", request.Text),
            new KeyValuePair<string, string>("to", ToBingLanguage(request.TargetLanguage)),
            new KeyValuePair<string, string>("fromLang", ToBingSourceLanguage(request.SourceLanguage)),
            new KeyValuePair<string, string>("token", token.Token),
            new KeyValuePair<string, string>("key", token.Key),
        };
        var message = new HttpRequestMessage(HttpMethod.Post, CreateTranslationUri(request.Endpoint, token))
        {
            Content = new FormUrlEncodedContent(fields),
        };
        AddCookie(message, cookie);
        return message;
    }

    private static Uri CreateTranslationUri(Uri endpoint, BingWebToken token)
    {
        var builder = new UriBuilder(endpoint)
        {
            Path = "/ttranslatev3",
            Query = "isVertical=1&IG=" + Uri.EscapeDataString(token.Ig) +
                "&IID=" + Uri.EscapeDataString(token.Iid),
        };
        return builder.Uri;
    }

    private static void AddCookie(HttpRequestMessage message, string? cookie)
    {
        if (!string.IsNullOrEmpty(cookie))
        {
            _ = message.Headers.TryAddWithoutValidation("Cookie", cookie);
        }
    }

    private static bool IsValidCookie(string? cookie) =>
        cookie is null ||
        (cookie.Length <= MaxCookieLength && !cookie.Contains('\r') && !cookie.Contains('\n'));

    private static bool TryReadToken(string html, out BingWebToken token)
    {
        token = default!;
        var ig = IgRegex().Match(html).Groups["value"].Value;
        var iid = IidRegex().Match(html).Groups["value"].Value;
        var helper = AbusePreventionRegex().Match(html);
        if (!IsValidProtocolValue(ig, MaxIdentifierLength) ||
            !IsValidProtocolValue(iid, MaxIdentifierLength) ||
            !helper.Success ||
            !IsValidProtocolValue(helper.Groups["key"].Value, MaxIdentifierLength) ||
            !IsValidProtocolValue(helper.Groups["token"].Value, MaxTokenLength) ||
            !long.TryParse(
                helper.Groups["expiration"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var expirationMilliseconds) ||
            expirationMilliseconds <= 0)
        {
            return false;
        }

        token = new BingWebToken(
            ig,
            iid,
            helper.Groups["key"].Value,
            helper.Groups["token"].Value);
        return true;
    }

    private static bool IsTokenInvalid(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("statusCode", out var statusCode) &&
                statusCode.ValueKind == JsonValueKind.Number &&
                statusCode.TryGetInt32(out var value) &&
                value == 205;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? TryReadTranslation(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Array ||
                document.RootElement.GetArrayLength() == 0)
            {
                return null;
            }

            var first = document.RootElement[0];
            if (first.ValueKind != JsonValueKind.Object ||
                !first.TryGetProperty("translations", out var translations) ||
                translations.ValueKind != JsonValueKind.Array ||
                translations.GetArrayLength() == 0)
            {
                return null;
            }

            var translation = translations[0];
            return translation.ValueKind == JsonValueKind.Object &&
                translation.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsValidProtocolValue(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Contains('\r') &&
        !value.Contains('\n');

    private static string ToBingSourceLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ||
        string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto-detect"
            : ToBingLanguage(language);

    private static string ToBingLanguage(string language)
    {
        var normalized = language.Replace('_', '-');
        if (string.Equals(normalized, "zh-Hans", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-Hans";
        }

        return string.Equals(normalized, "zh-Hant", StringComparison.OrdinalIgnoreCase)
            ? "zh-Hant"
            : normalized;
    }

    [GeneratedRegex("IG:\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex IgRegex();

    [GeneratedRegex("data-iid\\s*=\\s*\\\"(?<value>[^\\\"]+)\\\"", RegexOptions.CultureInvariant)]
    private static partial Regex IidRegex();

    [GeneratedRegex(
        "params_AbusePreventionHelper\\s*=\\s*\\[\\s*(?<key>[0-9]+)\\s*,\\s*\\\"(?<token>[^\\\"]+)\\\"\\s*,\\s*(?<expiration>[0-9]+)\\s*\\]",
        RegexOptions.CultureInvariant)]
    private static partial Regex AbusePreventionRegex();

    private sealed record BingWebToken(string Ig, string Iid, string Key, string Token);

    private sealed record BingTokenResult(BingWebToken? Token, TranslationStreamEvent? Failure)
    {
        public static BingTokenResult Succeeded(BingWebToken token) => new(token, null);

        public static BingTokenResult Failed(TranslationStreamEvent failure) => new(null, failure);
    }
}
