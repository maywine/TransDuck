// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Translation;

namespace TransDuck.Platform.Windows.Tests.Translation;

public sealed class OllamaProviderTests
{
    [Fact]
    public async Task TranslateAsync_StreamsNdjsonChunksAndCompletesAfterContentAndDoneSameLine()
    {
        const string payload = "{\"message\":{\"content\":\"first\"},\"done\":false}\n" +
                               "{\"message\":{\"content\":\"last\"},\"done\":true}\n";
        using var handler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "Bearer",
            static (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                payload,
                "application/x-ndjson")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new OllamaProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.Ollama));
        var captured = await handler.WaitForRequestAsync();
        using var body = JsonDocument.Parse(captured.Body!);

        Assert.True(captured.HasExpectedAuthorization);
        Assert.Contains("application/x-ndjson", captured.AcceptMediaTypes);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
        Assert.Collection(
            events,
            item => Assert.Equal("first", item.Text),
            item => Assert.Equal("last", item.Text),
            item => Assert.Equal(TranslationStreamEventKind.Completed, item.Kind));
    }

    [Fact]
    public async Task TranslateAsync_AllowsMissingOptionalAuthorization()
    {
        using var handler = new ProviderHttpMessageHandler(
            expectedApiKey: null,
            expectedAuthorizationScheme: null,
            static (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                "{\"done\":true}\n",
                "application/x-ndjson")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new OllamaProvider(httpClient);
        var request = ProviderTranslationTestSupport.Request(
            TranslationProviderIds.Ollama,
            credentials: new TranslationCredentials(null));

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, request);
        var captured = await handler.WaitForRequestAsync();

        Assert.False(captured.HasAuthorization);
        Assert.Equal(TranslationStreamEventKind.Completed, Assert.Single(events).Kind);
    }

    [Fact]
    public async Task TranslateAsync_MapsErrorMalformedAndPrematureEofToSafeFailures()
    {
        var cases = new[]
        {
            ("{\"error\":\"APIKEY_CANARY_PROVIDER_ADAPTER QUERY_CANARY_PROVIDER_ADAPTER\"}\n", QueryErrorCode.ProviderUnavailable, true),
            ("{malformed}\n", QueryErrorCode.Internal, false),
            ("{\"message\":{\"content\":\"partial\"},\"done\":false}\n", QueryErrorCode.ProviderUnavailable, true),
        };

        foreach (var testCase in cases)
        {
            using var handler = new ProviderHttpMessageHandler(
                ProviderTranslationTestSupport.ApiKey,
                "Bearer",
                (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(
                    HttpStatusCode.OK,
                    testCase.Item1,
                    "application/x-ndjson")));
            using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
            var provider = new OllamaProvider(httpClient);

            var events = await ProviderTranslationTestSupport.ReadEventsAsync(
                provider,
                ProviderTranslationTestSupport.Request(TranslationProviderIds.Ollama));

            if (events.Count == 2)
            {
                Assert.Equal("partial", events[0].Text);
                ProviderTranslationTestSupport.AssertFailure([events[1]], testCase.Item2, testCase.Item3);
            }
            else
            {
                ProviderTranslationTestSupport.AssertFailure(events, testCase.Item2, testCase.Item3);
            }
        }
    }

    [Fact]
    public async Task TranslateAsync_MapsCancellationAndTimeoutAndRejectsMissingModel()
    {
        using var cancellationHandler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "Bearer",
            static (_, token) => ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var cancellationClient = ProviderTranslationTestSupport.CreateHttpClient(cancellationHandler);
        var cancellationProvider = new OllamaProvider(cancellationClient);
        using var cancellation = new CancellationTokenSource();
        var cancellationTask = ProviderTranslationTestSupport.ReadEventsAsync(
            cancellationProvider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.Ollama),
            cancellation.Token);
        await cancellationHandler.WaitForRequestAsync();
        cancellation.Cancel();
        var cancelled = await cancellationTask;

        Assert.Equal(TranslationStreamEventKind.Cancelled, Assert.Single(cancelled).Kind);

        using var timeoutHandler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "Bearer",
            static (_, token) => ProviderTranslationTestSupport.WaitForCancellationAsync(token));
        using var timeoutClient = ProviderTranslationTestSupport.CreateHttpClient(timeoutHandler);
        var timeoutProvider = new OllamaProvider(timeoutClient);
        var timedOut = await ProviderTranslationTestSupport.ReadEventsAsync(
            timeoutProvider,
            ProviderTranslationTestSupport.Request(
                TranslationProviderIds.Ollama,
                timeout: TimeSpan.FromMilliseconds(150)));

        ProviderTranslationTestSupport.AssertFailure(timedOut, QueryErrorCode.Timeout, retryable: true);

        using var invalidHandler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "Bearer",
            static (_, _) => throw new InvalidOperationException("Invalid requests must not send HTTP."));
        using var invalidClient = ProviderTranslationTestSupport.CreateHttpClient(invalidHandler);
        var invalidProvider = new OllamaProvider(invalidClient);
        var invalidEvents = await ProviderTranslationTestSupport.ReadEventsAsync(
            invalidProvider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.Ollama, model: null));

        ProviderTranslationTestSupport.AssertFailure(invalidEvents, QueryErrorCode.InvalidRequest, retryable: false);
    }
}
