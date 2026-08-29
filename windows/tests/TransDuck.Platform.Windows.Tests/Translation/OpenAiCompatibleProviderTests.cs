// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Translation;

namespace TransDuck.Platform.Windows.Tests.Translation;

public sealed class OpenAiCompatibleProviderTests
{
    [Fact]
    public async Task TranslateAsync_RequiresModelAndDoesNotSendInvalidRequest()
    {
        using var handler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "Bearer",
            static (_, _) => throw new InvalidOperationException("Invalid requests must not send HTTP."));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new OpenAiCompatibleProvider(httpClient);
        var request = ProviderTranslationTestSupport.Request(TranslationProviderIds.OpenAiCompatible, model: null);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(provider, request);

        Assert.Equal(TranslationProviderIds.OpenAiCompatible, provider.Registration.Provider.ProviderId);
        Assert.Equal(ProviderCapability.Translation | ProviderCapability.Streaming, provider.Registration.Capabilities);
        ProviderTranslationTestSupport.AssertFailure(events, QueryErrorCode.InvalidRequest, retryable: false);
    }

    [Fact]
    public async Task TranslateAsync_AdaptsProviderRequestToOpenAiSse()
    {
        using var handler = new ProviderHttpMessageHandler(
            ProviderTranslationTestSupport.ApiKey,
            "Bearer",
            static (_, _) => Task.FromResult(ProviderTranslationTestSupport.Response(
                HttpStatusCode.OK,
                "data: {\"choices\":[{\"delta\":{\"content\":\"translated\"}}]}\n\ndata: [DONE]\n\n",
                "text/event-stream")));
        using var httpClient = ProviderTranslationTestSupport.CreateHttpClient(handler);
        var provider = new OpenAiCompatibleProvider(httpClient);

        var events = await ProviderTranslationTestSupport.ReadEventsAsync(
            provider,
            ProviderTranslationTestSupport.Request(TranslationProviderIds.OpenAiCompatible));
        var captured = await handler.WaitForRequestAsync();

        Assert.True(captured.HasExpectedAuthorization);
        Assert.Contains("text/event-stream", captured.AcceptMediaTypes);
        Assert.Collection(
            events,
            item => Assert.Equal("translated", item.Text),
            item => Assert.Equal(TranslationStreamEventKind.Completed, item.Kind));
    }
}
