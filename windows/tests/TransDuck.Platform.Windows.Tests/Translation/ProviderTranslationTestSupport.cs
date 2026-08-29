// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Platform.Windows.Tests.Translation;

internal static class ProviderTranslationTestSupport
{
    public const string ApiKey = "APIKEY_CANARY_PROVIDER_ADAPTER";
    public const string Query = "QUERY_CANARY_PROVIDER_ADAPTER";
    public static readonly Uri Endpoint = new("https://endpoint-canary.example.test/private/translation");

    public static TranslationProviderRequest Request(
        string providerId,
        string? model = "test-model",
        string? sourceLanguage = "en-US",
        string targetLanguage = "zh-Hans",
        TranslationCredentials? credentials = null,
        TimeSpan? timeout = null) => new(
        new ProviderDescriptor(providerId),
        Endpoint,
        model,
        Query,
        sourceLanguage,
        targetLanguage,
        credentials ?? new TranslationCredentials(ApiKey),
        timeout ?? TimeSpan.FromSeconds(2));

    public static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public static HttpResponseMessage Response(HttpStatusCode statusCode, string body, string mediaType = "application/json")
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)));
        content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        return new HttpResponseMessage(statusCode) { Content = content };
    }

    public static async Task<IReadOnlyList<TranslationStreamEvent>> ReadEventsAsync(
        ITranslationProvider provider,
        TranslationProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        var events = new List<TranslationStreamEvent>();
        await foreach (var item in provider.TranslateAsync(request, cancellationToken))
        {
            item.Validate();
            events.Add(item);
        }

        Assert.Single(events, item => item.IsTerminal);
        return events;
    }

    public static void AssertFailure(
        IReadOnlyList<TranslationStreamEvent> events,
        QueryErrorCode code,
        bool retryable)
    {
        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(code, failure.ErrorCode);
        Assert.Equal(retryable, failure.Retryable);
        Assert.False(failure.ErrorMessage!.Contains(ApiKey, StringComparison.Ordinal));
        Assert.False(failure.ErrorMessage.Contains(Query, StringComparison.Ordinal));
        Assert.False(failure.ErrorMessage.Contains(Endpoint.AbsoluteUri, StringComparison.Ordinal));
    }

    public static async Task<HttpResponseMessage> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("The fake response should be cancelled before it is sent.");
    }
}
