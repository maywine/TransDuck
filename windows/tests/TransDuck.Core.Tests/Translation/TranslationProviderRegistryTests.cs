// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Core.Tests.Translation;

public sealed class TranslationProviderRegistryTests
{
    [Fact]
    public void Register_ResolvesUniqueIdsAndListsProvidersInOrdinalOrder()
    {
        var registry = new TranslationProviderRegistry();
        var ollama = Provider(TranslationProviderIds.Ollama, ProviderCapability.Translation | ProviderCapability.Streaming);
        var deepL = Provider(TranslationProviderIds.DeepL, ProviderCapability.Translation);
        var bing = Provider(TranslationProviderIds.Bing, ProviderCapability.Translation);
        var google = Provider(TranslationProviderIds.Google, ProviderCapability.Translation);

        registry.Register(ollama);
        registry.Register(deepL);
        registry.Register(bing);
        registry.Register(google);

        Assert.True(registry.TryResolve(TranslationProviderIds.DeepL, out var byId));
        Assert.Same(deepL, byId);
        Assert.True(registry.TryResolve(new ProviderDescriptor(TranslationProviderIds.Ollama, "instance-a"), out var byDescriptor));
        Assert.Same(ollama, byDescriptor);
        Assert.False(registry.TryResolve(string.Empty, out var missing));
        Assert.Null(missing);
        Assert.True(registry.TryResolve(TranslationProviderIds.Bing, out var byBingId));
        Assert.Same(bing, byBingId);
        Assert.True(registry.TryResolve(TranslationProviderIds.Google, out var byGoogleId));
        Assert.Same(google, byGoogleId);
        Assert.Equal(new[] { "bing", "deepl", "google", "ollama" }, registry.List()
            .Select(provider => provider.Registration.Provider.ProviderId));
    }

    [Fact]
    public void WebProviderIds_AreStableAndHaveNoImplicitStreamingCapability()
    {
        var bing = Provider(TranslationProviderIds.Bing, ProviderCapability.Translation);
        var google = Provider(TranslationProviderIds.Google, ProviderCapability.Translation);

        Assert.Equal("bing", TranslationProviderIds.Bing);
        Assert.Equal("google", TranslationProviderIds.Google);
        Assert.Equal(ProviderCapability.Translation, bing.Registration.Capabilities);
        Assert.Equal(ProviderCapability.Translation, google.Registration.Capabilities);
        Assert.False((bing.Registration.Capabilities & ProviderCapability.Streaming) != 0);
        Assert.False((google.Registration.Capabilities & ProviderCapability.Streaming) != 0);
    }

    [Fact]
    public void Register_RejectsMissingTranslationCapabilityAndDuplicateId()
    {
        var registry = new TranslationProviderRegistry();

        Assert.Throws<ArgumentException>(() => registry.Register(Provider("non-translation", ProviderCapability.Ocr)));
        registry.Register(Provider(TranslationProviderIds.Ollama, ProviderCapability.Translation));

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(Provider(TranslationProviderIds.Ollama, ProviderCapability.Translation)));
    }

    [Fact]
    public async Task Register_IsConcurrencySafeAndDeterministic()
    {
        var registry = new TranslationProviderRegistry();
        var unique = Enumerable.Range(0, 16)
            .Select(index => Task.Run(() => registry.Register(
                Provider($"provider-{index:D2}", ProviderCapability.Translation))))
            .ToArray();

        await Task.WhenAll(unique);

        var duplicates = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            try
            {
                registry.Register(Provider("duplicate-provider", ProviderCapability.Translation));
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        })));

        Assert.Equal(1, duplicates.Count(success => success));
        Assert.Equal(
            registry.List().Select(provider => provider.Registration.Provider.ProviderId)
                .OrderBy(id => id, StringComparer.Ordinal),
            registry.List().Select(provider => provider.Registration.Provider.ProviderId));
    }

    [Fact]
    public void TranslationStreamEvents_EnforceSingleTerminalShapes()
    {
        TranslationStreamEvent.Delta("chunk").Validate();
        TranslationStreamEvent.Completed().Validate();
        TranslationStreamEvent.Cancelled().Validate();
        TranslationStreamEvent.Failed("safe", QueryErrorCode.Timeout, retryable: true).Validate();

        Assert.Throws<InvalidOperationException>(() =>
            new TranslationStreamEvent(TranslationStreamEventKind.Completed, ErrorMessage: "unexpected").Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new TranslationStreamEvent(TranslationStreamEventKind.Failed, ErrorMessage: "safe").Validate());
    }

    private static TestProvider Provider(string providerId, ProviderCapability capabilities) =>
        new(new ProviderRegistration(new ProviderDescriptor(providerId), capabilities));

    private sealed class TestProvider(ProviderRegistration registration) : ITranslationProvider
    {
        public ProviderRegistration Registration { get; } = registration;

        public IAsyncEnumerable<TranslationStreamEvent> TranslateAsync(
            TranslationProviderRequest request,
            CancellationToken cancellationToken) => Empty();

        private static async IAsyncEnumerable<TranslationStreamEvent> Empty()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
