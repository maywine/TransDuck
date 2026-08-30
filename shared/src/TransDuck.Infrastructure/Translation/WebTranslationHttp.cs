// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using TransDuck.Core.Translation;

namespace TransDuck.Infrastructure.Translation;

/// <summary>
/// Reads bounded web-provider responses without exposing upstream content in failure events.
/// </summary>
internal static class WebTranslationHttp
{
    public const int MaxResponseBytes = 1024 * 1024;

    public static async Task<WebTranslationHttpResponse> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage message,
        CancellationToken requestToken,
        CancellationToken callerToken,
        CancellationToken timeoutToken)
    {
        HttpResponseMessage? response = null;
        Exception? sendFailure = null;
        try
        {
            response = await httpClient.SendAsync(
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
            return WebTranslationHttpResponse.Failed(TranslationProviderFailures.FromException(
                sendFailure,
                callerToken,
                timeoutToken));
        }

        if (response is null)
        {
            return WebTranslationHttpResponse.Failed(TranslationProviderFailures.Internal());
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return WebTranslationHttpResponse.Failed(
                    TranslationProviderFailures.FromHttpStatus(response.StatusCode));
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                return WebTranslationHttpResponse.Failed(TranslationProviderFailures.Internal());
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
                return WebTranslationHttpResponse.Failed(TranslationProviderFailures.FromException(
                    contentFailure,
                    callerToken,
                    timeoutToken));
            }

            return payload is null
                ? WebTranslationHttpResponse.Failed(TranslationProviderFailures.Internal())
                : WebTranslationHttpResponse.Succeeded(response.StatusCode, payload);
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
                return new UTF8Encoding(false, true).GetString(
                    output.GetBuffer(),
                    0,
                    checked((int)output.Length));
            }

            if (output.Length + read > MaxResponseBytes)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }
}

internal sealed record WebTranslationHttpResponse(
    HttpStatusCode? StatusCode,
    string? Payload,
    TranslationStreamEvent? Failure)
{
    public static WebTranslationHttpResponse Succeeded(HttpStatusCode statusCode, string payload) =>
        new(statusCode, payload, null);

    public static WebTranslationHttpResponse Failed(TranslationStreamEvent failure) =>
        new(null, null, failure);
}
