// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Platform.Windows.Translation;

/// <summary>
/// Adapts DeepL's non-streaming JSON translation response into delta and completed events.
/// </summary>
public sealed class DeepLProvider : ITranslationProvider
{
    private const int MaxResponseBytes = 1024 * 1024;
    private readonly ITranslationHttpClientLeaseSource _clientLeaseSource;

    /// <summary>Creates a provider using an externally owned HttpClient.</summary>
    public DeepLProvider(HttpClient httpClient)
        : this(new FixedTranslationHttpClientLeaseSource(httpClient))
    {
    }

    /// <summary>Creates a provider using one application-owned transport lease source.</summary>
    public DeepLProvider(ITranslationHttpClientLeaseSource clientLeaseSource)
    {
        _clientLeaseSource = clientLeaseSource ?? throw new ArgumentNullException(nameof(clientLeaseSource));
    }

    /// <inheritdoc />
    public ProviderRegistration Registration { get; } = new(
        new ProviderDescriptor(TranslationProviderIds.DeepL),
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

        if (!request.Credentials.HasApiKey)
        {
            yield return TranslationProviderFailures.Authentication();
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
        var requestToken = linkedSource.Token;
        using var message = CreateRequest(request);

        HttpResponseMessage? response = null;
        Exception? sendFailure = null;
        try
        {
            response = await clientLease.Client.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                requestToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (TranslationProviderFailures.IsRecoverable(exception))
        {
            sendFailure = exception;
        }

        if (sendFailure is not null)
        {
            yield return TranslationProviderFailures.FromException(
                sendFailure,
                cancellationToken,
                timeoutSource.Token);
            yield break;
        }

        if (response is null)
        {
            yield return TranslationProviderFailures.Internal();
            yield break;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                yield return TranslationProviderFailures.FromHttpStatus(response.StatusCode);
                yield break;
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                yield return TranslationProviderFailures.Internal();
                yield break;
            }

            string? payload = null;
            Exception? contentFailure = null;
            try
            {
                payload = await ReadBoundedResponseAsync(response.Content, requestToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (TranslationProviderFailures.IsRecoverable(exception))
            {
                contentFailure = exception;
            }

            if (contentFailure is not null)
            {
                yield return TranslationProviderFailures.FromException(
                    contentFailure,
                    cancellationToken,
                    timeoutSource.Token);
                yield break;
            }

            if (payload is null)
            {
                yield return TranslationProviderFailures.Internal();
                yield break;
            }

            var text = TryReadTranslation(payload);
            if (string.IsNullOrEmpty(text))
            {
                yield return TranslationProviderFailures.Internal();
                yield break;
            }

            yield return TranslationStreamEvent.Delta(text);
            yield return TranslationStreamEvent.Completed();
        }
    }

    private static bool TryValidateRequest(TranslationProviderRequest? request)
    {
        if (request is null)
        {
            return false;
        }

        try
        {
            request.ValidateForProvider(TranslationProviderIds.DeepL, modelRequired: false);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or ContractValidationException)
        {
            return false;
        }
    }

    private static HttpRequestMessage CreateRequest(TranslationProviderRequest request)
    {
        var payload = new Dictionary<string, object>
        {
            ["text"] = new[] { request.Text },
            ["target_lang"] = ToDeepLTargetLanguage(request.TargetLanguage),
        };
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
        {
            payload["source_lang"] = ToDeepLSourceLanguage(request.SourceLanguage);
        }

        var message = new HttpRequestMessage(HttpMethod.Post, request.Endpoint)
        {
            Content = JsonContent.Create(payload),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "DeepL-Auth-Key",
            request.Credentials.GetApiKey());
        return message;
    }

    private static string? TryReadTranslation(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("translations", out var translations) ||
                translations.ValueKind != JsonValueKind.Array ||
                translations.GetArrayLength() == 0)
            {
                return null;
            }

            var first = translations[0];
            return first.TryGetProperty("text", out var text) &&
                text.ValueKind == JsonValueKind.String
                ? text.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[81920];
        using var output = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, checked((int)output.Length));
            }

            if (output.Length + read > MaxResponseBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ToDeepLSourceLanguage(string languageTag) =>
        languageTag.Split('-', 2)[0].ToUpperInvariant();

    private static string ToDeepLTargetLanguage(string languageTag)
    {
        var normalized = languageTag.Replace('_', '-').ToUpperInvariant();
        return normalized switch
        {
            "EN-US" or "EN-GB" or "PT-BR" or "PT-PT" => normalized,
            _ => ToDeepLSourceLanguage(normalized),
        };
    }
}
