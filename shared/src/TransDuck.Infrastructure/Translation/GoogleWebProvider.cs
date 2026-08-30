// Copyright (c) 2026 maywine. All rights reserved.

using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Infrastructure.Translation;

/// <summary>
/// Adapts the unofficial Google Translate GTX web response into non-streaming translation events.
/// </summary>
public sealed class GoogleWebProvider : ITranslationProvider
{
    public const string DefaultEndpoint = "https://translate.google.com/translate_a/single";

    private readonly ITranslationHttpClientLeaseSource _clientLeaseSource;

    /// <summary>Creates a provider using an externally owned HttpClient.</summary>
    public GoogleWebProvider(HttpClient httpClient)
        : this(new FixedTranslationHttpClientLeaseSource(httpClient))
    {
    }

    /// <summary>Creates a provider using one application-owned transport lease source.</summary>
    public GoogleWebProvider(ITranslationHttpClientLeaseSource clientLeaseSource)
    {
        _clientLeaseSource = clientLeaseSource ?? throw new ArgumentNullException(nameof(clientLeaseSource));
    }

    /// <inheritdoc />
    public ProviderRegistration Registration { get; } = new(
        new ProviderDescriptor(TranslationProviderIds.Google),
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
        using var message = new HttpRequestMessage(HttpMethod.Get, CreateRequestUri(request));
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

        var text = TryReadTranslation(response.Payload!);
        if (string.IsNullOrEmpty(text))
        {
            yield return TranslationProviderFailures.Internal();
            yield break;
        }

        yield return TranslationStreamEvent.Delta(text);
        yield return TranslationStreamEvent.Completed();
    }

    private static bool TryValidateRequest(TranslationProviderRequest? request)
    {
        if (request is null || string.Equals(request.TargetLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            request.ValidateForProvider(TranslationProviderIds.Google, modelRequired: false);
            return string.IsNullOrEmpty(request.Endpoint.UserInfo) &&
                string.IsNullOrEmpty(request.Endpoint.Query) &&
                string.IsNullOrEmpty(request.Endpoint.Fragment);
        }
        catch (Exception exception) when (exception is ArgumentException or ContractValidationException)
        {
            return false;
        }
    }

    private static Uri CreateRequestUri(TranslationProviderRequest request)
    {
        var query = new[]
        {
            "client=gtx",
            "dj=1",
            "dt=t",
            "ie=UTF-8",
            "sl=" + Uri.EscapeDataString(ToGoogleSourceLanguage(request.SourceLanguage)),
            "tl=" + Uri.EscapeDataString(ToGoogleLanguage(request.TargetLanguage)),
            "q=" + Uri.EscapeDataString(request.Text),
        };
        var builder = new UriBuilder(request.Endpoint)
        {
            Query = string.Join("&", query),
        };
        return builder.Uri;
    }

    private static string? TryReadTranslation(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("sentences", out var sentences) ||
                sentences.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var text = new StringBuilder();
            foreach (var sentence in sentences.EnumerateArray())
            {
                if (sentence.ValueKind != JsonValueKind.Object ||
                    !sentence.TryGetProperty("trans", out var translation) ||
                    translation.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                text.Append(translation.GetString());
            }

            return text.Length == 0 ? null : text.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToGoogleSourceLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ||
        string.Equals(language, "auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : ToGoogleLanguage(language);

    private static string ToGoogleLanguage(string language)
    {
        var normalized = language.Replace('_', '-');
        if (string.Equals(normalized, "zh-Hans", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-CN";
        }

        return string.Equals(normalized, "zh-Hant", StringComparison.OrdinalIgnoreCase)
            ? "zh-TW"
            : normalized;
    }
}
