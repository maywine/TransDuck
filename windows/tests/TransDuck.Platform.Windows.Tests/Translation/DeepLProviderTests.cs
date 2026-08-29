// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Translation;

namespace TransDuck.Platform.Windows.Tests.Translation;

public sealed class DeepLProviderTests
{
    [Fact]
    public async Task TranslateAsync_UsesDeepLHeaderAndCanonicalSourceTargetBody()
    {
        using var handler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "DeepL-Auth-Key",
            static (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                "{\"translations\":[{\"text\":\"translated\"}]}")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new DeepLProvider(httpClient);
        var request = ProviderTranslationTestSupport.Request(
            TranslationProviderIds.DeepL,
            model: null,
            sourceLanguage: "en-US",
            targetLanguage: "pt-BR");

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, request);
        var captured = await handler.WaitForRequestAsync();
        using var body = JsonDocument.Parse(captured.Body!);

        Assert.True(captured.HasExpectedAuthorization);
        Assert.Equal("DeepL-Auth-Key", captured.AuthorizationScheme);
        Assert.Equal("EN", body.RootElement.GetProperty("source_lang").GetString());
        Assert.Equal("PT-BR", body.RootElement.GetProperty("target_lang").GetString());
        Assert.Equal(ProviderTranslationTestSupport.Query, body.RootElement.GetProperty("text")[0].GetString());
        Assert.Collection(
            events,
            item => Assert.Equal("translated", item.Text),
            item => Assert.Equal(TranslationStreamEventKind.Completed, item.Kind));
    }

    [Fact]
    public async Task TranslateAsync_OmitsOptionalSourceLanguageAndDoesNotExposeCredentialInCapture()
    {
        using var handler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "DeepL-Auth-Key",
            static (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                "{\"translations\":[{\"text\":\"translated\"}]}")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new DeepLProvider(httpClient);
        var request = ProviderTranslationTestSupport.Request(TranslationProviderIds.DeepL, model: null, sourceLanguage: null);

        _ = await ProviderTranslationTestSupport.ReadEventsAsync(provider, request);
        var captured = await handler.WaitForRequestAsync();
        var capturedBody = Assert.IsType<string>(captured.Body);
        using var body = JsonDocument.Parse(capturedBody);

        Assert.False(body.RootElement.TryGetProperty("source_lang", out _));
        Assert.False(captured.ToString().Contains(ProviderTranslationTestSupport.ApiKey, StringComparison.Ordinal));
        Assert.False(capturedBody.Contains(ProviderTranslationTestSupport.ApiKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, QueryErrorCode.Authentication, false)]
    [InlineData(HttpStatusCode.TooManyRequests, QueryErrorCode.RateLimited, true)]
    [InlineData(HttpStatusCode.BadRequest, QueryErrorCode.InvalidRequest, false)]
    [InlineData(HttpStatusCode.BadGateway, QueryErrorCode.ProviderUnavailable, true)]
    [InlineData(HttpStatusCode.RequestTimeout, QueryErrorCode.Timeout, true)]
    public async Task TranslateAsync_MapsHttpStatusToSafeRetryableFailure(
        HttpStatusCode statusCode,
        QueryErrorCode errorCode,
        bool retryable)
    {
        using var handler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "DeepL-Auth-Key",
            (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(
                statusCode,
                $"{{\"message\":\"{ProviderTranslationTestSupport.ApiKey} {ProviderTranslationTestSupport.Query}\"}}")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new DeepLProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.DeepL, model: null));

        ProviderTranslationTestSupport.AssertFailure(events, errorCode, retryable);
    }

    [Fact]
    public async Task TranslateAsync_RejectsMalformedAndOversizedSuccessBodiesWithoutReadingRawPayload()
    {
        using var malformedHandler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "DeepL-Auth-Key",
            static (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(HttpStatusCode.OK, "{malformed")));
        using var malformedClient = ProviderTranslationTestSupport.CreateHttpClient(malformedHandler);
        var malformedProvider = new DeepLProvider(malformedClient);
        var malformedEvents = await ProviderTranslationTestSupport.ReadEventsAsync(
            malformedProvider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.DeepL, model: null));

        ProviderTranslationTestSupport.AssertFailure(malformedEvents, QueryErrorCode.Internal, retryable: false);

        using var oversizedHandler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "DeepL-Auth-Key",
            static (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OversizedResponseContent(1024 * 1024 + 1),
            }));
        using var oversizedClient = ProviderTranslationTestSupport.CreateHttpClient(oversizedHandler);
        var oversizedProvider = new DeepLProvider(oversizedClient);
        var oversizedEvents = await ProviderTranslationTestSupport.ReadEventsAsync(
            oversizedProvider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.DeepL, model: null));

        ProviderTranslationTestSupport.AssertFailure(oversizedEvents, QueryErrorCode.Internal, retryable: false);
    }

    [Fact]
    public async Task TranslateAsync_MapsCallerCancellationAndTimeout()
    {
        using var cancellationHandler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "DeepL-Auth-Key",
            static (_, token) => ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var cancellationClient = ProviderTranslationTestSupport.CreateHttpClient(cancellationHandler);
        var cancellationProvider = new DeepLProvider(cancellationClient);
        using var cancellation = new CancellationTokenSource();

        var cancellationTask = ProviderTranslationTestSupport.ReadEventsAsync(
            cancellationProvider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.DeepL, model: null),
            cancellation.Token);
        await cancellationHandler.WaitForRequestAsync();
        cancellation.Cancel();
        var cancelled = await cancellationTask;

        Assert.Equal(TranslationStreamEventKind.Cancelled, Assert.Single(cancelled).Kind);

        using var timeoutHandler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "DeepL-Auth-Key",
            static (_, token) => ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var timeoutClient = ProviderTranslationTestSupport.CreateHttpClient(timeoutHandler);
        var timeoutProvider = new DeepLProvider(timeoutClient);
        var timedOut = await ProviderTranslationTestSupport.ReadEventsAsync(
            timeoutProvider,
            ProviderTranslationTestSupport.Request(
                TranslationProviderIds.DeepL,
                model: null,
                timeout: TimeSpan.FromMilliseconds(150)));

        ProviderTranslationTestSupport.AssertFailure(timedOut, QueryErrorCode.Timeout, retryable: true);
    }

    [Fact]
    public async Task TranslateAsync_RequiresCredentialBeforeSendingHttp()
    {
        using var handler = new ProviderHttpMessageHandler(
            expectedApiKey: null,
            expectedAuthorizationScheme: null,
            static (_, _) => throw new InvalidOperationException("Missing DeepL credentials must not send HTTP."));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new DeepLProvider(httpClient);
        var request = ProviderTranslationTestSupport.Request(
            TranslationProviderIds.DeepL,
            model: null,
            credentials: new TranslationCredentials(null));

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, request);

        ProviderTranslationTestSupport.AssertFailure(events, QueryErrorCode.Authentication, retryable: false);
    }
}
