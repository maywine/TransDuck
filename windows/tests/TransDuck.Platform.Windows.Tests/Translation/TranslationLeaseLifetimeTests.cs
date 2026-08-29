// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Proxy;
using TransDuck.Platform.Windows.Translation;

namespace TransDuck.Platform.Windows.Tests.Translation;

public sealed class TranslationLeaseLifetimeTests
{
    private const string QueryCanary = "QUERY_CANARY_LEASE_LIFETIME";
    private const string LeaseFailureCanary = "LEASE_SOURCE_FAILURE_CANARY";

    [Fact]
    public async Task OpenAiSse_CompletionKeepsLeaseThroughFirstDeltaAndReleasesExactlyOnceAtEnumerationEnd()
    {
        var content = new GatedSseContent();
        using var httpClient = CreateHttpClient(new ResponseHandler(() => SseResponse(content)));
        var source = new TrackingLeaseSource(httpClient);
        var client = new OpenAiCompatibleSseClient(source);
        await using var enumerator = client.TranslateAsync(OpenAiRequest(), CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(TranslationStreamEventKind.Delta, enumerator.Current.Kind);
        Assert.Equal("first", enumerator.Current.Text);
        Assert.Single(source.Leases);
        Assert.Equal(0, source.Leases[0].DisposeCount);

        content.ReleaseCompletion();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(TranslationStreamEventKind.Completed, enumerator.Current.Kind);
        Assert.False(await enumerator.MoveNextAsync());
        Assert.Equal(1, source.Leases[0].DisposeCount);
    }

    [Fact]
    public async Task OpenAiSse_CancellationAndEnumeratorDisposalReleaseEachLeaseExactlyOnce()
    {
        var cancellationContent = new GatedSseContent();
        using var cancellationClient = CreateHttpClient(new ResponseHandler(() => SseResponse(cancellationContent)));
        var cancellationSource = new TrackingLeaseSource(cancellationClient);
        var cancellationClientAdapter = new OpenAiCompatibleSseClient(cancellationSource);
        using var cancellation = new CancellationTokenSource();
        await using var cancellationEnumerator = cancellationClientAdapter
            .TranslateAsync(OpenAiRequest(), cancellation.Token)
            .GetAsyncEnumerator();

        Assert.True(await cancellationEnumerator.MoveNextAsync());
        cancellation.Cancel();
        Assert.True(await cancellationEnumerator.MoveNextAsync());
        Assert.Equal(TranslationStreamEventKind.Cancelled, cancellationEnumerator.Current.Kind);
        Assert.False(await cancellationEnumerator.MoveNextAsync());
        Assert.Equal(1, Assert.Single(cancellationSource.Leases).DisposeCount);

        var disposeContent = new GatedSseContent();
        using var disposeClient = CreateHttpClient(new ResponseHandler(() => SseResponse(disposeContent)));
        var disposeSource = new TrackingLeaseSource(disposeClient);
        var disposeClientAdapter = new OpenAiCompatibleSseClient(disposeSource);
        var disposeEnumerator = disposeClientAdapter
            .TranslateAsync(OpenAiRequest(), CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await disposeEnumerator.MoveNextAsync());
        Assert.Equal(0, Assert.Single(disposeSource.Leases).DisposeCount);
        await disposeEnumerator.DisposeAsync();
        Assert.Equal(1, Assert.Single(disposeSource.Leases).DisposeCount);
    }

    [Fact]
    public async Task ProxyPoolUpdate_NewOpenAiRequestUsesNewGenerationWhileOldStreamKeepsOldHandler()
    {
        var oldContent = new GatedSseContent();
        var factory = new GenerationHandlerFactory(
            () => SseResponse(oldContent),
            () => CompletedSseResponse("new"));
        using var pool = new ProxyHttpClientPool(CustomProxy("http://proxy-one.example.test:8080"), factory);
        var client = new OpenAiCompatibleSseClient(new ProxyTranslationHttpClientLeaseSource(pool));
        await using var oldEnumerator = client.TranslateAsync(OpenAiRequest(), CancellationToken.None).GetAsyncEnumerator();

        Assert.True(await oldEnumerator.MoveNextAsync());
        Assert.Equal("first", oldEnumerator.Current.Text);
        Assert.Equal(1, factory.RoutedHandlers[0].RequestCount);
        Assert.Equal(0, factory.RoutedHandlers[0].DisposeCount);

        _ = pool.Update(CustomProxy("http://proxy-two.example.test:8081"));
        var newEvents = await ReadEventsAsync(client, OpenAiRequest());

        Assert.Equal("new", Assert.Single(newEvents, item => item.Kind == TranslationStreamEventKind.Delta).Text);
        Assert.Equal(1, factory.RoutedHandlers[1].RequestCount);
        Assert.Equal(0, factory.RoutedHandlers[0].DisposeCount);

        oldContent.ReleaseCompletion();
        Assert.True(await oldEnumerator.MoveNextAsync());
        Assert.Equal(TranslationStreamEventKind.Completed, oldEnumerator.Current.Kind);
        Assert.False(await oldEnumerator.MoveNextAsync());
        Assert.Equal(1, factory.RoutedHandlers[0].DisposeCount);
    }

    [Fact]
    public async Task Bing205Retry_AcquiresOneLeaseForTheEntireFetchAndPostSequence()
    {
        using var handler = new WebProviderHttpMessageHandler((_, requestNumber, _) => Task.FromResult(requestNumber switch
        {
            0 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK, TokenHtml("IG_FIRST", "IID_FIRST", "123456", "TOKEN_FIRST")),
            1 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK, """{"statusCode":205}"""),
            2 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK, TokenHtml("IG_SECOND", "IID_SECOND", "654321", "TOKEN_SECOND")),
            3 => ProviderTranslationTestSupport.Response(HttpStatusCode.OK, """[{"translations":[{"text":"translated"}]}]"""),
            _ => throw new InvalidOperationException("Bing 205 may issue only one token refresh sequence."),
        }));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var source = new TrackingLeaseSource(httpClient);
        var provider = new BingWebProvider(source);
        var endpoint = new Uri(BingWebProvider.DefaultEndpoint);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            ProviderRequest(TranslationProviderIds.Bing, endpoint, credentials: new TranslationCredentials("COOKIE_CANARY")));

        Assert.Equal("translated", Assert.Single(events, item => item.Kind == TranslationStreamEventKind.Delta).Text);
        Assert.Equal(new[] { endpoint }, source.Endpoints);
        Assert.Single(source.Leases);
        Assert.Equal(1, source.Leases[0].DisposeCount);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task Google_AcquiresTheUnmodifiedConfiguredEndpointExactlyOnce()
    {
        using var handler = new WebProviderHttpMessageHandler(static (_, requestNumber, _) =>
        {
            Assert.Equal(0, requestNumber);
            return Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                """{"sentences":[{"trans":"translated"}]}"""));
        });
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var source = new TrackingLeaseSource(httpClient);
        var provider = new GoogleWebProvider(source);
        var endpoint = new Uri("https://google-endpoint.example.test/translate_a/single");

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            ProviderRequest(TranslationProviderIds.Google, endpoint, credentials: new TranslationCredentials("IGNORED_CREDENTIAL_CANARY")));

        Assert.Equal("translated", Assert.Single(events, item => item.Kind == TranslationStreamEventKind.Delta).Text);
        Assert.Equal(new[] { endpoint }, source.Endpoints);
        Assert.Single(source.Leases);
        Assert.Equal(1, source.Leases[0].DisposeCount);
    }

    [Fact]
    public async Task LeaseSourceFailure_MapsToClosedProviderUnavailableWithoutLeakingExceptionOrRequestData()
    {
        var source = new ThrowingLeaseSource();
        var provider = new GoogleWebProvider(source);
        var endpoint = new Uri("https://endpoint-canary.example.test/translate_a/single");

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            ProviderRequest(TranslationProviderIds.Google, endpoint, credentials: new TranslationCredentials("CREDENTIAL_CANARY")));

        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(QueryErrorCode.ProviderUnavailable, failure.ErrorCode);
        Assert.True(failure.Retryable);
        Assert.DoesNotContain(LeaseFailureCanary, failure.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(QueryCanary, failure.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(endpoint.AbsoluteUri, failure.ErrorMessage, StringComparison.Ordinal);
    }

    private static TranslationRequest OpenAiRequest() => new(
        new Uri("https://provider.example.test/v1/chat/completions"),
        "model",
        QueryCanary,
        "en-US",
        "zh-Hans",
        new TranslationCredentials("APIKEY_CANARY"),
        TimeSpan.FromSeconds(5));

    private static TranslationProviderRequest ProviderRequest(
        string providerId,
        Uri endpoint,
        TranslationCredentials credentials) => new(
        new ProviderDescriptor(providerId),
        endpoint,
        Model: null,
        Text: QueryCanary,
        SourceLanguage: "en-US",
        TargetLanguage: "zh-Hans",
        Credentials: credentials,
        Timeout: TimeSpan.FromSeconds(5));

    private static WindowsProxySettings CustomProxy(string value) => new(
        WindowsProxySettingsMigration.CurrentVersion,
        WindowsProxyMode.CustomHttp,
        new Uri(value));

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static HttpResponseMessage SseResponse(HttpContent content) => new(HttpStatusCode.OK)
    {
        Content = content,
    };

    private static HttpResponseMessage CompletedSseResponse(string text)
    {
        var content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(
            "data: {\"choices\":[{\"delta\":{\"content\":\"" + text + "\"}}]}\n\n" +
            "data: [DONE]\n\n")));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return SseResponse(content);
    }

    private static async Task<IReadOnlyList<TranslationStreamEvent>> ReadEventsAsync(
        OpenAiCompatibleSseClient client,
        TranslationRequest request)
    {
        var events = new List<TranslationStreamEvent>();
        await foreach (var item in client.TranslateAsync(request, CancellationToken.None))
        {
            events.Add(item);
        }

        return events;
    }

    private static string TokenHtml(string ig, string iid, string key, string token) =>
        $$"""
        <html>IG: "{{ig}}" <div data-iid="{{iid}}"></div>
        <script>var params_AbusePreventionHelper = [{{key}}, "{{token}}", 3600000];</script>
        </html>
        """;

    private sealed class TrackingLeaseSource : ITranslationHttpClientLeaseSource
    {
        private readonly HttpClient _client;

        public TrackingLeaseSource(HttpClient client)
        {
            _client = client;
        }

        public List<Uri> Endpoints { get; } = [];

        public List<TrackingLease> Leases { get; } = [];

        public ITranslationHttpClientLease AcquireLease(Uri endpoint)
        {
            Endpoints.Add(endpoint);
            var lease = new TrackingLease(_client);
            Leases.Add(lease);
            return lease;
        }
    }

    private sealed class ThrowingLeaseSource : ITranslationHttpClientLeaseSource
    {
        public ITranslationHttpClientLease AcquireLease(Uri endpoint) =>
            throw new InvalidOperationException(LeaseFailureCanary);
    }

    private sealed class TrackingLease(HttpClient client) : ITranslationHttpClientLease
    {
        private int _disposeCount;

        public HttpClient Client { get; } = client;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class ResponseHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory());
    }

    private sealed class GenerationHandlerFactory(
        Func<HttpResponseMessage> firstResponse,
        Func<HttpResponseMessage> secondResponse) : IProxyHttpMessageHandlerFactory
    {
        private readonly Queue<Func<HttpResponseMessage>> _routedResponses = new([firstResponse, secondResponse]);

        public List<TrackingResponseHandler> RoutedHandlers { get; } = [];

        public HttpMessageHandler Create(ProxyHttpTransportOptions options)
        {
            if (options.Mode == WindowsProxyMode.CustomHttp)
            {
                var handler = new TrackingResponseHandler(_routedResponses.Dequeue());
                RoutedHandlers.Add(handler);
                return handler;
            }

            return new TrackingResponseHandler(() => throw new InvalidOperationException("Loopback transport is not used."));
        }
    }

    private sealed class TrackingResponseHandler(Func<HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _disposeCount;
        private int _requestCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(responseFactory());
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }

            base.Dispose(disposing);
        }
    }

    private sealed class GatedSseContent : HttpContent
    {
        private readonly GatedSseStream _stream = new();

        public GatedSseContent()
        {
            Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        }

        public void ReleaseCompletion() => _stream.ReleaseCompletion();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new NotSupportedException("The test response is consumed as a stream."));

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(_stream);

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(_stream);
    }

    private sealed class GatedSseStream : Stream
    {
        private static readonly byte[] First = Encoding.UTF8.GetBytes(
            "data: {\"choices\":[{\"delta\":{\"content\":\"first\"}}]}\n\n");
        private static readonly byte[] Completion = "data: [DONE]\n\n"u8.ToArray();
        private readonly TaskCompletionSource _completionReleased = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stage;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void ReleaseCompletion() => _completionReleased.TrySetResult();

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadCoreAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ReadCoreAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private async ValueTask<int> ReadCoreAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var stage = Volatile.Read(ref _stage);
            if (stage == 0)
            {
                Volatile.Write(ref _stage, 1);
                First.CopyTo(buffer);
                return First.Length;
            }

            if (stage == 1)
            {
                await _completionReleased.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                Volatile.Write(ref _stage, 2);
                Completion.CopyTo(buffer);
                return Completion.Length;
            }

            Volatile.Write(ref _stage, 3);
            return 0;
        }
    }
}
