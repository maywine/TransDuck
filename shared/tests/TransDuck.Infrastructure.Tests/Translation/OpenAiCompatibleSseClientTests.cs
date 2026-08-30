// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Translation;

namespace TransDuck.Infrastructure.Tests.Translation;

public sealed class OpenAiCompatibleSseClientTests
{
    private const string TestApiKey = "unit-test-key-not-for-output";

    [Fact]
    public async Task TranslateAsync_CreatesAuthenticatedStreamingPostRequest()
    {
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            static (_, _) => Task.FromResult(SseResponse("data: [DONE]\n\n")));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());
        var captured = await handler.WaitForRequestAsync();

        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(CreateRequest().Endpoint, captured.RequestUri);
        Assert.True(captured.AcceptsServerSentEvents);
        Assert.True(captured.HasAuthorization);
        Assert.True(captured.HasExpectedBearerAuthorization);
        Assert.NotNull(captured.Body);
        using var requestBody = JsonDocument.Parse(captured.Body);
        Assert.Equal("test-model", requestBody.RootElement.GetProperty("model").GetString());
        Assert.True(requestBody.RootElement.GetProperty("stream").GetBoolean());
        Assert.False(captured.Body.Contains(TestApiKey, StringComparison.Ordinal));
        Assert.False(captured.ToString().Contains(TestApiKey, StringComparison.Ordinal));
        Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Completed, events[0].Kind);
    }

    [Fact]
    public async Task TranslateAsync_WithoutApiKeyDoesNotSendAuthorization()
    {
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            static (_, _) => Task.FromResult(SseResponse("data: [DONE]\n\n")));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);
        var request = CreateRequest() with { Credentials = new TranslationCredentials(null) };

        var events = await ReadEventsAsync(client, request);
        var captured = await handler.WaitForRequestAsync();

        Assert.False(captured.HasAuthorization);
        Assert.False(captured.HasExpectedBearerAuthorization);
        Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Completed, events[0].Kind);
    }

    [Fact]
    public async Task TranslateAsync_ParsesCrLfAndCompletesOnDone()
    {
        const string payload = "data: {\"choices\":[{\"delta\":{\"content\":\"你好\"}}]}\r\n\r\n" +
                               "data: [DONE]\r\n\r\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        Assert.Collection(
            events,
            item =>
            {
                Assert.Equal(TranslationStreamEventKind.Delta, item.Kind);
                Assert.Equal("你好", item.Text);
            },
            item => Assert.Equal(TranslationStreamEventKind.Completed, item.Kind));
    }

    [Fact]
    public async Task TranslateAsync_CombinesMultilineDataFields()
    {
        const string payload = "data: {\"choices\":[\n" +
                               "data: {\"delta\":{\"content\":\"combined\"}}]}\n\n" +
                               "data: [DONE]\n\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        Assert.Collection(
            events,
            item =>
            {
                Assert.Equal(TranslationStreamEventKind.Delta, item.Kind);
                Assert.Equal("combined", item.Text);
            },
            item => Assert.Equal(TranslationStreamEventKind.Completed, item.Kind));
    }

    [Fact]
    public async Task TranslateAsync_RemovesOnlyOneOptionalSpaceAfterDataColon()
    {
        const string payload = "data:  [DONE]\n\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(QueryErrorCode.Internal, failure.ErrorCode);
        Assert.False(failure.Retryable);
    }

    [Fact]
    public async Task TranslateAsync_CompletesOnFinishReason()
    {
        const string payload = "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        var completed = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Completed, completed.Kind);
    }

    [Fact]
    public async Task TranslateAsync_MapsErrorEventToFailure()
    {
        const string payload = "event: error\ndata: {\"message\":\"provider unavailable\"}\n\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(QueryErrorCode.ProviderUnavailable, failure.ErrorCode);
        Assert.True(failure.Retryable);
    }

    [Fact]
    public async Task TranslateAsync_MapsHttpErrorResponseToFailure()
    {
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("{\"error\":{\"message\":\"upstream unavailable\"}}"),
            }));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(QueryErrorCode.ProviderUnavailable, failure.ErrorCode);
        Assert.True(failure.Retryable);
    }

    [Fact]
    public async Task TranslateAsync_ReportsMalformedJsonAsFailure()
    {
        const string payload = "data: {not-json}\n\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(QueryErrorCode.Internal, failure.ErrorCode);
        Assert.False(failure.Retryable);
    }

    [Fact]
    public async Task TranslateAsync_ReportsCallerCancellation()
    {
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            static (_, cancellationToken) => WaitForCancellationAsync(cancellationToken));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);
        using var cancellation = new CancellationTokenSource();

        var eventsTask = ReadEventsAsync(client, CreateRequest(), cancellation.Token);
        await handler.WaitForRequestAsync();
        cancellation.Cancel();
        var events = await eventsTask;

        var cancelled = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Cancelled, cancelled.Kind);
    }

    [Fact]
    public async Task TranslateAsync_ReportsCallerCancellationWhileReadingResponseStream()
    {
        var content = new BlockingReadStreamContent();
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);
        using var cancellation = new CancellationTokenSource();

        var eventsTask = ReadEventsAsync(client, CreateRequest(), cancellation.Token);
        await content.WaitForReadAsync();
        cancellation.Cancel();
        var events = await eventsTask;

        var cancelled = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Cancelled, cancelled.Kind);
    }

    [Fact]
    public async Task TranslateAsync_ReportsRequestTimeout()
    {
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            static (_, cancellationToken) => WaitForCancellationAsync(cancellationToken));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);
        var request = CreateRequest() with { Timeout = TimeSpan.FromMilliseconds(150) };

        var events = await ReadEventsAsync(client, request);

        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(QueryErrorCode.Timeout, failure.ErrorCode);
        Assert.True(failure.Retryable);
    }

    [Fact]
    public async Task TranslateAsync_ReportsRequestTimeoutWhileReadingResponseStream()
    {
        var content = new BlockingReadStreamContent();
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);
        var request = CreateRequest() with { Timeout = TimeSpan.FromMilliseconds(150) };

        var eventsTask = ReadEventsAsync(client, request);
        await content.WaitForReadAsync();
        var events = await eventsTask;

        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(QueryErrorCode.Timeout, failure.ErrorCode);
        Assert.True(failure.Retryable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, QueryErrorCode.Authentication, false)]
    [InlineData(HttpStatusCode.TooManyRequests, QueryErrorCode.RateLimited, true)]
    [InlineData(HttpStatusCode.BadRequest, QueryErrorCode.InvalidRequest, false)]
    [InlineData(HttpStatusCode.BadGateway, QueryErrorCode.ProviderUnavailable, true)]
    public async Task TranslateAsync_MapsHttpStatusToFixedSafeFailure(
        HttpStatusCode statusCode,
        QueryErrorCode errorCode,
        bool retryable)
    {
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent($"{{\"error\":{{\"message\":\"{TestApiKey}\"}}}}"),
            }));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        AssertFixedFailure(events, errorCode, retryable);
    }

    [Fact]
    public async Task TranslateAsync_MapsTransportExceptionWithoutLeakingCanaries()
    {
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            static (_, _) => throw new HttpRequestException("APIKEY_CANARY_PROVIDER_ADAPTER QUERY_CANARY_PROVIDER_ADAPTER"));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        AssertFixedFailure(events, QueryErrorCode.Network, retryable: true);
    }

    [Fact]
    public async Task TranslateAsync_ReportsPrematureEofAsProviderUnavailable()
    {
        const string payload = "data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        Assert.Equal("partial", events[0].Text);
        AssertFixedFailure([events[1]], QueryErrorCode.ProviderUnavailable, retryable: true);
    }

    [Fact]
    public async Task TranslateAsync_EmitsDeltaThenCompletedWhenFinishReasonSharesChunk()
    {
        const string payload = "data: {\"choices\":[{\"delta\":{\"content\":\"last\"},\"finish_reason\":\"stop\"}]}\n\n";
        using var handler = new FakeHttpMessageHandler(
            TestApiKey,
            (_, _) => Task.FromResult(SseResponse(payload)));
        using var httpClient = CreateHttpClient(handler);
        var client = new OpenAiCompatibleSseClient(httpClient);

        var events = await ReadEventsAsync(client, CreateRequest());

        Assert.Collection(
            events,
            item => Assert.Equal("last", item.Text),
            item => Assert.Equal(TranslationStreamEventKind.Completed, item.Kind));
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static TranslationRequest CreateRequest() => new(
        new Uri("https://example.test/v1/chat/completions"),
        "test-model",
        "source text",
        "en",
        "zh-Hans",
        new TranslationCredentials(TestApiKey),
        TimeSpan.FromSeconds(2));

    private static async Task<IReadOnlyList<TranslationStreamEvent>> ReadEventsAsync(
        OpenAiCompatibleSseClient client,
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        var events = new List<TranslationStreamEvent>();
        await foreach (var streamEvent in client.TranslateAsync(request, cancellationToken))
        {
            events.Add(streamEvent);
        }

        return events;
    }

    private static HttpResponseMessage SseResponse(string payload)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(payload)));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static async Task<HttpResponseMessage> WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        throw new InvalidOperationException("The test response should be cancelled before it is sent.");
    }

    private static void AssertFixedFailure(
        IReadOnlyList<TranslationStreamEvent> events,
        QueryErrorCode errorCode,
        bool retryable)
    {
        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(errorCode, failure.ErrorCode);
        Assert.Equal(retryable, failure.Retryable);
        Assert.False(failure.ErrorMessage!.Contains(TestApiKey, StringComparison.Ordinal));
        Assert.False(failure.ErrorMessage.Contains("APIKEY_CANARY_PROVIDER_ADAPTER", StringComparison.Ordinal));
        Assert.False(failure.ErrorMessage.Contains("QUERY_CANARY_PROVIDER_ADAPTER", StringComparison.Ordinal));
    }
}
