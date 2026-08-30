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

namespace TransDuck.Infrastructure.Translation;

/// <summary>
/// Streams Ollama NDJSON chat responses from a configured full endpoint.
/// </summary>
public sealed class OllamaProvider : ITranslationProvider
{
    private readonly ITranslationHttpClientLeaseSource _clientLeaseSource;

    /// <summary>Creates a provider using an externally owned HttpClient.</summary>
    public OllamaProvider(HttpClient httpClient)
        : this(new FixedTranslationHttpClientLeaseSource(httpClient))
    {
    }

    /// <summary>Creates a provider using one application-owned transport lease source.</summary>
    public OllamaProvider(ITranslationHttpClientLeaseSource clientLeaseSource)
    {
        _clientLeaseSource = clientLeaseSource ?? throw new ArgumentNullException(nameof(clientLeaseSource));
    }

    /// <inheritdoc />
    public ProviderRegistration Registration { get; } = new(
        new ProviderDescriptor(TranslationProviderIds.Ollama),
        ProviderCapability.Translation | ProviderCapability.Streaming);

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

            Stream? stream = null;
            Exception? streamFailure = null;
            try
            {
                stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (TranslationProviderFailures.IsRecoverable(exception))
            {
                streamFailure = exception;
            }

            if (streamFailure is not null)
            {
                yield return TranslationProviderFailures.FromException(
                    streamFailure,
                    cancellationToken,
                    timeoutSource.Token);
                yield break;
            }

            if (stream is null)
            {
                yield return TranslationProviderFailures.Internal();
                yield break;
            }

            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
                       bufferSize: 4096, leaveOpen: false))
            {
                while (true)
                {
                    string? line = null;
                    Exception? readFailure = null;
                    try
                    {
                        line = await reader.ReadLineAsync(requestToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (TranslationProviderFailures.IsRecoverable(exception))
                    {
                        readFailure = exception;
                    }

                    if (readFailure is not null)
                    {
                        yield return TranslationProviderFailures.FromException(
                            readFailure,
                            cancellationToken,
                            timeoutSource.Token);
                        yield break;
                    }

                    if (line is null)
                    {
                        yield return TranslationProviderFailures.ProviderUnavailable();
                        yield break;
                    }

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    var parsed = ParseLine(line, out var completed);
                    if (parsed is not null)
                    {
                        yield return parsed;
                        if (parsed.IsTerminal)
                        {
                            yield break;
                        }
                    }

                    if (completed)
                    {
                        yield return TranslationStreamEvent.Completed();
                        yield break;
                    }
                }
            }
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
            request.ValidateForProvider(TranslationProviderIds.Ollama, modelRequired: true);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or ContractValidationException)
        {
            return false;
        }
    }

    private static HttpRequestMessage CreateRequest(TranslationProviderRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, request.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = request.Model,
                stream = true,
                messages = new[]
                {
                    new { role = "system", content = BuildSystemPrompt(request) },
                    new { role = "user", content = request.Text },
                },
            }),
        };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));
        var apiKey = request.Credentials.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return message;
    }

    private static TranslationStreamEvent? ParseLine(string line, out bool completed)
    {
        completed = false;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("error", out _))
            {
                return TranslationProviderFailures.ProviderUnavailable();
            }

            completed = root.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True;

            if (root.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.Object &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    return TranslationStreamEvent.Delta(text);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return TranslationProviderFailures.Internal();
        }
    }

    private static string BuildSystemPrompt(TranslationProviderRequest request)
    {
        var source = string.IsNullOrWhiteSpace(request.SourceLanguage) ? "auto-detected" : request.SourceLanguage;
        return $"Translate the user text from {source} into {request.TargetLanguage}. Return only the translation.";
    }
}
