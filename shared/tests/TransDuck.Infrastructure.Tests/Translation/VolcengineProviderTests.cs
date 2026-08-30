// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Translation;

namespace TransDuck.Infrastructure.Tests.Translation;

public sealed class VolcengineProviderTests
{
    private const string AccessKeyId = "AKLT_TEST_ACCESS_KEY";
    private const string SecretAccessKey = "SECRET_TEST_KEY";
    private static readonly DateTimeOffset SigningTime =
        new(2024, 6, 19, 7, 13, 6, TimeSpan.Zero);

    [Fact]
    public void CredentialCodec_RoundTripsBothKeysAndRejectsMalformedValuesWithoutPrintingThem()
    {
        var encoded = VolcengineCredentialCodec.Encode(AccessKeyId, SecretAccessKey);

        Assert.True(VolcengineCredentialCodec.TryDecode(encoded, out var credentials));
        Assert.Equal(AccessKeyId, credentials.GetApiKey());
        Assert.Equal(SecretAccessKey, credentials.GetSecretKey());
        Assert.DoesNotContain(AccessKeyId, credentials.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SecretAccessKey, credentials.ToString(), StringComparison.Ordinal);
        Assert.False(VolcengineCredentialCodec.TryDecode("volcengine:v1:not-base64:also-not-base64", out _));
        Assert.False(VolcengineCredentialCodec.TryDecode("unversioned", out _));
    }

    [Fact]
    public async Task TranslateAsync_SignsCanonicalRequestAndReadsDocumentedResponse()
    {
        using var handler = new CapturingHandler(static (_, _) => Task.FromResult(
            ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                """{"TranslationList":[{"Translation":"translated","DetectedSourceLanguage":"en"}],"ResponseMetadata":{"Error":null}}""")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new VolcengineProvider(httpClient, new FixedTimeProvider(SigningTime));

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, Request());
        var captured = await handler.WaitForRequestAsync();

        Assert.Equal(VolcengineProvider.DefaultEndpoint, "https://translate.volcengineapi.com/");
        Assert.Equal(TranslationProviderIds.Volcengine, provider.Registration.Provider.ProviderId);
        Assert.Equal(HttpMethod.Post, captured.Method);
        Assert.Equal(
            "https://translate.volcengineapi.com/?Action=TranslateText&Version=2020-06-01",
            captured.RequestUri.AbsoluteUri);
        Assert.Equal("translate.volcengineapi.com", captured.Host);
        Assert.Equal("application/json", captured.ContentType);
        Assert.Equal("20240619T071306Z", captured.XDate);
        Assert.Equal(
            "e96bb69f1610a54b680d348b3f448570be21dacc26e7386f62b357d7af02c600",
            captured.XContentSha256);
        Assert.Equal(
            "HMAC-SHA256 Credential=AKLT_TEST_ACCESS_KEY/20240619/cn-north-1/translate/request, " +
            "SignedHeaders=content-type;host;x-content-sha256;x-date, " +
            "Signature=92db4297fa54da416a50f8f788b0c61705532d953af92529031d49295bb935da",
            captured.Authorization);
        Assert.Equal(
            """{"SourceLanguage":"en","TargetLanguage":"zh","TextList":["QUERY_CANARY_PROVIDER_ADAPTER"]}""",
            captured.Body);
        Assert.DoesNotContain(SecretAccessKey, captured.ToString(), StringComparison.Ordinal);
        Assert.Collection(
            events,
            item => Assert.Equal("translated", item.Text),
            item => Assert.Equal(TranslationStreamEventKind.Completed, item.Kind));
    }

    [Fact]
    public async Task TranslateAsync_OmitsAutomaticSourceAndAcceptsNestedCurrentResponseShape()
    {
        using var handler = new CapturingHandler(static (_, _) => Task.FromResult(
            ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                """{"Result":{"TranslationList":[{"Translation":"繁體"}]},"ResponseMetadata":{}}""")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new VolcengineProvider(httpClient, new FixedTimeProvider(SigningTime));

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            Request(sourceLanguage: null, targetLanguage: "zh-TW"));
        var captured = await handler.WaitForRequestAsync();
        using var body = JsonDocument.Parse(captured.Body);

        Assert.False(body.RootElement.TryGetProperty("SourceLanguage", out _));
        Assert.Equal("zh-Hant", body.RootElement.GetProperty("TargetLanguage").GetString());
        Assert.Equal("繁體", Assert.Single(events, item => item.Kind == TranslationStreamEventKind.Delta).Text);
    }

    [Theory]
    [InlineData("SignatureDoesNotMatch", QueryErrorCode.Authentication, false)]
    [InlineData("-429", QueryErrorCode.RateLimited, true)]
    [InlineData("InvalidParameter", QueryErrorCode.InvalidRequest, false)]
    [InlineData("InternalError", QueryErrorCode.ProviderUnavailable, true)]
    public async Task TranslateAsync_MapsServiceErrorsWithoutExposingUpstreamText(
        string serviceCode,
        QueryErrorCode expectedCode,
        bool retryable)
    {
        using var handler = new CapturingHandler((_, _) => Task.FromResult(
            ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(new
                {
                    ResponseMetadata = new
                    {
                        Error = new
                        {
                            Code = serviceCode,
                            Message = SecretAccessKey + " " + ProviderTranslationTestSupport.Query,
                        },
                    },
                }))));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new VolcengineProvider(httpClient, new FixedTimeProvider(SigningTime));

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, Request());

        AssertFailure(events, expectedCode, retryable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, QueryErrorCode.Authentication, false)]
    [InlineData(HttpStatusCode.TooManyRequests, QueryErrorCode.RateLimited, true)]
    [InlineData(HttpStatusCode.BadGateway, QueryErrorCode.ProviderUnavailable, true)]
    public async Task TranslateAsync_MapsHttpErrors(
        HttpStatusCode statusCode,
        QueryErrorCode expectedCode,
        bool retryable)
    {
        using var handler = new CapturingHandler((_, _) => Task.FromResult(
            ProviderTranslationTestSupport.Response(statusCode, SecretAccessKey)));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new VolcengineProvider(httpClient, new FixedTimeProvider(SigningTime));

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, Request());

        AssertFailure(events, expectedCode, retryable);
    }

    [Fact]
    public async Task TranslateAsync_RejectsMalformedAndOversizedSuccessBodies()
    {
        using var malformedHandler = new CapturingHandler(static (_, _) => Task.FromResult(
            ProviderTranslationTestSupport.Response(HttpStatusCode.OK, "{malformed")));
        using var malformedClient = ProviderTranslationTestSupport.CreateHttpClient(malformedHandler);
        var malformedProvider = new VolcengineProvider(malformedClient, new FixedTimeProvider(SigningTime));

        var malformed = await ProviderTranslationTestSupport.ReadEventsAsync(malformedProvider, Request());

        AssertFailure(malformed, QueryErrorCode.Internal, retryable: false);

        using var oversizedHandler = new CapturingHandler(static (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OversizedResponseContent(1024 * 1024 + 1),
            }));
        using var oversizedClient = ProviderTranslationTestSupport.CreateHttpClient(oversizedHandler);
        var oversizedProvider = new VolcengineProvider(oversizedClient, new FixedTimeProvider(SigningTime));

        var oversized = await ProviderTranslationTestSupport.ReadEventsAsync(oversizedProvider, Request());

        AssertFailure(oversized, QueryErrorCode.Internal, retryable: false);
    }

    [Fact]
    public async Task TranslateAsync_MapsCallerCancellationAndTimeout()
    {
        using var cancellationHandler = new CapturingHandler(
            static (_, token) => ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var cancellationClient = ProviderTranslationTestSupport.CreateHttpClient(cancellationHandler);
        var cancellationProvider = new VolcengineProvider(
            cancellationClient,
            new FixedTimeProvider(SigningTime));
        using var cancellation = new CancellationTokenSource();
        var cancellationTask = ProviderTranslationTestSupport.ReadEventsAsync(
            cancellationProvider,
            Request(),
            cancellation.Token);
        await cancellationHandler.WaitForRequestAsync();

        cancellation.Cancel();
        var cancelled = await cancellationTask;

        Assert.Equal(TranslationStreamEventKind.Cancelled, Assert.Single(cancelled).Kind);

        using var timeoutHandler = new CapturingHandler(
            static (_, token) => ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var timeoutClient = ProviderTranslationTestSupport.CreateHttpClient(timeoutHandler);
        var timeoutProvider = new VolcengineProvider(timeoutClient, new FixedTimeProvider(SigningTime));

        var timedOut = await ProviderTranslationTestSupport.ReadEventsAsync(
            timeoutProvider,
            Request(timeout: TimeSpan.FromMilliseconds(150)));

        AssertFailure(timedOut, QueryErrorCode.Timeout, retryable: true);
    }

    [Theory]
    [InlineData(null, SecretAccessKey)]
    [InlineData(AccessKeyId, null)]
    public async Task TranslateAsync_RequiresBothSigningCredentials(string? accessKeyId, string? secretAccessKey)
    {
        using var handler = new CapturingHandler(
            static (_, _) => throw new InvalidOperationException("Incomplete credentials must not send HTTP."));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new VolcengineProvider(httpClient, new FixedTimeProvider(SigningTime));

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            Request(credentials: new TranslationCredentials(accessKeyId, secretAccessKey)));

        AssertFailure(events, QueryErrorCode.Authentication, retryable: false);
    }

    private static TranslationProviderRequest Request(
        string? sourceLanguage = "en-US",
        string targetLanguage = "zh-Hans",
        TranslationCredentials? credentials = null,
        TimeSpan? timeout = null) => new(
        new ProviderDescriptor(TranslationProviderIds.Volcengine),
        new Uri(VolcengineProvider.DefaultEndpoint),
        Model: null,
        Text: ProviderTranslationTestSupport.Query,
        SourceLanguage: sourceLanguage,
        TargetLanguage: targetLanguage,
        Credentials: credentials ?? new TranslationCredentials(AccessKeyId, SecretAccessKey),
        Timeout: timeout ?? TimeSpan.FromSeconds(2));

    private static void AssertFailure(
        IReadOnlyList<TranslationStreamEvent> events,
        QueryErrorCode code,
        bool retryable)
    {
        var failure = Assert.Single(events);
        Assert.Equal(TranslationStreamEventKind.Failed, failure.Kind);
        Assert.Equal(code, failure.ErrorCode);
        Assert.Equal(retryable, failure.Retryable);
        Assert.DoesNotContain(AccessKeyId, failure.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretAccessKey, failure.ErrorMessage!, StringComparison.Ordinal);
        Assert.DoesNotContain(ProviderTranslationTestSupport.Query, failure.ErrorMessage!, StringComparison.Ordinal);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class CapturingHandler(
        Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        private readonly TaskCompletionSource<CapturedRequest> _requestReceived = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CapturedRequest> WaitForRequestAsync() => _requestReceived.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Host,
                request.Content?.Headers.ContentType?.MediaType,
                Header(request, "X-Date"),
                Header(request, "X-Content-Sha256"),
                Header(request, "Authorization"),
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            _requestReceived.TrySetResult(captured);
            return await responseFactory(captured, cancellationToken).ConfigureAwait(false);
        }

        private static string Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values)
                ? Assert.Single(values)
                : string.Empty;
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string? Host,
        string? ContentType,
        string XDate,
        string XContentSha256,
        string Authorization,
        string Body);
}
