// Copyright (c) 2026 maywine. All rights reserved.

using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Infrastructure.Translation;

/// <summary>
/// Adapts Volcengine's signed, non-streaming text translation API into translation events.
/// </summary>
public sealed class VolcengineProvider : ITranslationProvider
{
    private const int MaxResponseBytes = 1024 * 1024;
    private const string Action = "TranslateText";
    private const string ApiVersion = "2020-06-01";
    private const string Region = "cn-north-1";
    private const string Service = "translate";
    private const string ContentType = "application/json";
    private const string SignedHeaders = "content-type;host;x-content-sha256;x-date";
    private const string CanonicalQuery = $"Action={Action}&Version={ApiVersion}";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly ITranslationHttpClientLeaseSource _clientLeaseSource;
    private readonly TimeProvider _timeProvider;

    /// <summary>Gets the default Volcengine text translation endpoint.</summary>
    public const string DefaultEndpoint = "https://translate.volcengineapi.com/";

    /// <summary>Creates a provider using an externally owned HttpClient.</summary>
    public VolcengineProvider(HttpClient httpClient, TimeProvider? timeProvider = null)
        : this(new FixedTranslationHttpClientLeaseSource(httpClient), timeProvider)
    {
    }

    /// <summary>Creates a provider using one application-owned transport lease source.</summary>
    public VolcengineProvider(
        ITranslationHttpClientLeaseSource clientLeaseSource,
        TimeProvider? timeProvider = null)
    {
        _clientLeaseSource = clientLeaseSource ?? throw new ArgumentNullException(nameof(clientLeaseSource));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ProviderRegistration Registration { get; } = new(
        new ProviderDescriptor(TranslationProviderIds.Volcengine),
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

        if (!request.Credentials.HasApiKey || !request.Credentials.HasSecretKey)
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
        using var message = CreateRequest(request, _timeProvider.GetUtcNow());

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

            var failure = ParseResponse(payload, out var translation);
            if (failure is not null)
            {
                yield return failure;
                yield break;
            }

            if (string.IsNullOrEmpty(translation))
            {
                yield return TranslationProviderFailures.Internal();
                yield break;
            }

            yield return TranslationStreamEvent.Delta(translation);
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
            request.ValidateForProvider(TranslationProviderIds.Volcengine, modelRequired: false);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or ContractValidationException)
        {
            return false;
        }
    }

    private static HttpRequestMessage CreateRequest(
        TranslationProviderRequest request,
        DateTimeOffset timestamp)
    {
        var body = CreateBody(request);
        var bodyHash = HashHex(body);
        var xDate = timestamp.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var shortDate = xDate[..8];
        var host = request.Endpoint.Host.ToLowerInvariant();
        if (!request.Endpoint.IsDefaultPort)
        {
            host += ":" + request.Endpoint.Port.ToString(CultureInfo.InvariantCulture);
        }
        var path = string.IsNullOrEmpty(request.Endpoint.AbsolutePath)
            ? "/"
            : request.Endpoint.AbsolutePath;
        var canonicalHeaders =
            $"content-type:{ContentType}\n" +
            $"host:{host}\n" +
            $"x-content-sha256:{bodyHash}\n" +
            $"x-date:{xDate}\n";
        var canonicalRequest =
            $"POST\n{path}\n{CanonicalQuery}\n{canonicalHeaders}\n{SignedHeaders}\n{bodyHash}";
        var credentialScope = $"{shortDate}/{Region}/{Service}/request";
        var stringToSign =
            $"HMAC-SHA256\n{xDate}\n{credentialScope}\n{HashHex(Utf8.GetBytes(canonicalRequest))}";
        var signature = CreateSignature(
            request.Credentials.GetSecretKey()!,
            shortDate,
            stringToSign);
        var endpoint = new UriBuilder(request.Endpoint)
        {
            Query = CanonicalQuery,
        }.Uri;
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body),
        };
        message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ContentType);
        message.Headers.Host = host;
        message.Headers.Add("X-Date", xDate);
        message.Headers.Add("X-Content-Sha256", bodyHash);
        message.Headers.TryAddWithoutValidation(
            "Authorization",
            $"HMAC-SHA256 Credential={request.Credentials.GetApiKey()}/{credentialScope}, " +
            $"SignedHeaders={SignedHeaders}, Signature={signature}");
        return message;
    }

    private static byte[] CreateBody(TranslationProviderRequest request)
    {
        var payload = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage))
        {
            payload["SourceLanguage"] = ToVolcengineLanguage(request.SourceLanguage);
        }

        payload["TargetLanguage"] = ToVolcengineLanguage(request.TargetLanguage);
        payload["TextList"] = new[] { request.Text };
        return JsonSerializer.SerializeToUtf8Bytes(payload);
    }

    private static string ToVolcengineLanguage(string language)
    {
        var normalized = language.Replace('_', '-');
        if (normalized.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("zh-HK", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("zh-MO", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-Hant";
        }

        if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh";
        }

        var separator = normalized.IndexOf('-');
        return (separator < 0 ? normalized : normalized[..separator]).ToLowerInvariant();
    }

    private static string CreateSignature(
        string secretAccessKey,
        string shortDate,
        string stringToSign)
    {
        byte[]? dateKey = null;
        byte[]? regionKey = null;
        byte[]? serviceKey = null;
        byte[]? signingKey = null;
        byte[]? secretKeyBytes = null;
        try
        {
            secretKeyBytes = Utf8.GetBytes(secretAccessKey);
            dateKey = Hmac(secretKeyBytes, shortDate);
            regionKey = Hmac(dateKey, Region);
            serviceKey = Hmac(regionKey, Service);
            signingKey = Hmac(serviceKey, "request");
            return Convert.ToHexString(HMACSHA256.HashData(
                signingKey,
                Utf8.GetBytes(stringToSign))).ToLowerInvariant();
        }
        finally
        {
            Zero(secretKeyBytes);
            Zero(dateKey);
            Zero(regionKey);
            Zero(serviceKey);
            Zero(signingKey);
        }
    }

    private static byte[] Hmac(byte[] key, string value) =>
        HMACSHA256.HashData(key, Utf8.GetBytes(value));

    private static string HashHex(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static void Zero(byte[]? value)
    {
        if (value is not null)
        {
            CryptographicOperations.ZeroMemory(value);
        }
    }

    private static TranslationStreamEvent? ParseResponse(string payload, out string? translation)
    {
        translation = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (TryGetServiceError(root, out var error))
            {
                return MapServiceError(error);
            }

            var result = root.TryGetProperty("Result", out var nestedResult) &&
                nestedResult.ValueKind == JsonValueKind.Object
                ? nestedResult
                : root;
            if (!result.TryGetProperty("TranslationList", out var translations) ||
                translations.ValueKind != JsonValueKind.Array ||
                translations.GetArrayLength() == 0)
            {
                return TranslationProviderFailures.Internal();
            }

            var first = translations[0];
            if (!first.TryGetProperty("Translation", out var text) ||
                text.ValueKind != JsonValueKind.String)
            {
                return TranslationProviderFailures.Internal();
            }

            translation = text.GetString();
            return null;
        }
        catch (JsonException)
        {
            return TranslationProviderFailures.Internal();
        }
    }

    private static bool TryGetServiceError(JsonElement root, out JsonElement error)
    {
        error = default;
        return root.TryGetProperty("ResponseMetadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object &&
            metadata.TryGetProperty("Error", out error) &&
            error.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
    }

    private static TranslationStreamEvent MapServiceError(JsonElement error)
    {
        var code = error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("Code", out var codeElement)
            ? codeElement.ToString()
            : string.Empty;
        if (code.Contains("429", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("RateLimit", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("TooMany", StringComparison.OrdinalIgnoreCase))
        {
            return TranslationProviderFailures.RateLimited();
        }

        if (code.Contains("Access", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Auth", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Credential", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Signature", StringComparison.OrdinalIgnoreCase))
        {
            return TranslationProviderFailures.Authentication();
        }

        if (code.Contains("Internal", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("Unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return TranslationProviderFailures.ProviderUnavailable();
        }

        return TranslationProviderFailures.InvalidRequest();
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
                return Utf8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
            }

            if (output.Length + read > MaxResponseBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}
