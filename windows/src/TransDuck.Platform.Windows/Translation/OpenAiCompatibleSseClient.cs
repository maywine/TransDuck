// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TransDuck.Core.Translation;

namespace TransDuck.Platform.Windows.Translation;

/// <summary>
/// Streams an OpenAI-compatible chat-completions response without retaining endpoint credentials.
/// </summary>
public sealed class OpenAiCompatibleSseClient : IStreamingTranslationService
{
    private readonly ITranslationHttpClientLeaseSource _clientLeaseSource;

    /// <summary>Creates a client using an externally owned HttpClient.</summary>
    public OpenAiCompatibleSseClient(HttpClient httpClient)
        : this(new FixedTranslationHttpClientLeaseSource(httpClient))
    {
    }

    /// <summary>Creates a client using one application-owned transport lease source.</summary>
    public OpenAiCompatibleSseClient(ITranslationHttpClientLeaseSource clientLeaseSource)
    {
        _clientLeaseSource = clientLeaseSource ?? throw new ArgumentNullException(nameof(clientLeaseSource));
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TranslationStreamEvent> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        return StreamAsync(request, cancellationToken);
    }

    private async IAsyncEnumerable<TranslationStreamEvent> StreamAsync(
        TranslationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (request is null)
        {
            yield return TranslationProviderFailures.InvalidRequest();
            yield break;
        }

        var requestIsValid = true;
        try
        {
            request.Validate();
        }
        catch (ArgumentException)
        {
            requestIsValid = false;
        }

        if (!requestIsValid)
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
        using var httpRequest = CreateRequest(request);

        HttpResponseMessage? response = null;
        Exception? sendFailure = null;
        try
        {
            response = await clientLease.Client.SendAsync(
                httpRequest,
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
            await using (var events = SseEventReader.ReadAsync(stream, requestToken)
                                   .GetAsyncEnumerator(requestToken))
            {
                while (true)
                {
                    ServerSentEvent item = default!;
                    var hasNext = false;
                    Exception? readFailure = null;
                    try
                    {
                        hasNext = await events.MoveNextAsync().ConfigureAwait(false);
                        item = hasNext ? events.Current : default!;
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

                    if (!hasNext)
                    {
                        yield return TranslationProviderFailures.ProviderUnavailable();
                        yield break;
                    }

                    var parsed = ParseEvent(item, out var completed);
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

    private static HttpRequestMessage CreateRequest(TranslationRequest request)
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
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var apiKey = request.Credentials.GetApiKey();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return message;
    }

    private static string BuildSystemPrompt(TranslationRequest request)
    {
        var source = string.IsNullOrWhiteSpace(request.SourceLanguage) ? "auto-detected" : request.SourceLanguage;
        var target = string.IsNullOrWhiteSpace(request.TargetLanguage) ? "the requested target language" : request.TargetLanguage;
        return $"Translate the user text from {source} into {target}. Return only the translation.";
    }

    private static TranslationStreamEvent? ParseEvent(ServerSentEvent item, out bool completed)
    {
        completed = string.Equals(item.Data, "[DONE]", StringComparison.Ordinal);
        if (completed)
        {
            return null;
        }

        if (string.Equals(item.EventName, "error", StringComparison.OrdinalIgnoreCase))
        {
            return TranslationProviderFailures.ProviderUnavailable();
        }

        try
        {
            using var document = JsonDocument.Parse(item.Data);
            if (document.RootElement.TryGetProperty("error", out _))
            {
                return TranslationProviderFailures.ProviderUnavailable();
            }

            if (!document.RootElement.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var choice = choices[0];
            completed = choice.TryGetProperty("finish_reason", out var finishReason) &&
                finishReason.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
            if (choice.TryGetProperty("delta", out var delta) &&
                delta.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                return string.IsNullOrEmpty(text) ? null : TranslationStreamEvent.Delta(text);
            }

            return null;
        }
        catch (JsonException)
        {
            return TranslationProviderFailures.Internal();
        }
    }
}
