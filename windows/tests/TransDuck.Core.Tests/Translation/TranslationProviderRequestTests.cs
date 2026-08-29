// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Core.Tests.Translation;

public sealed class TranslationProviderRequestTests
{
    [Fact]
    public void Validate_AllowsOptionalModelAndSourceLanguage()
    {
        var request = CreateRequest(TranslationProviderIds.DeepL) with
        {
            Model = null,
            SourceLanguage = null,
        };

        request.ValidateForProvider(TranslationProviderIds.DeepL, modelRequired: false);
    }

    [Theory]
    [InlineData("bing")]
    [InlineData("google")]
    public void ValidateForProvider_WebProvidersAllowOptionalModels(string providerId)
    {
        var request = CreateRequest(providerId) with { Model = null };

        request.ValidateForProvider(providerId, modelRequired: false);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsSuppliedEmptyModel(string model)
    {
        var request = CreateRequest(TranslationProviderIds.Ollama) with { Model = model };

        var exception = Assert.Throws<ArgumentException>(request.Validate);

        Assert.Equal(nameof(TranslationProviderRequest.Model), exception.ParamName);
    }

    [Fact]
    public void ValidateForProvider_RejectsMismatchedProviderAndMissingRequiredModel()
    {
        var request = CreateRequest(TranslationProviderIds.DeepL) with { Model = null };

        var provider = Assert.Throws<ArgumentException>(() =>
            request.ValidateForProvider(TranslationProviderIds.Ollama, modelRequired: true));
        var model = Assert.Throws<ArgumentException>(() =>
            (request with { Provider = new ProviderDescriptor(TranslationProviderIds.Ollama) })
            .ValidateForProvider(TranslationProviderIds.Ollama, modelRequired: true));

        Assert.Equal(nameof(TranslationProviderRequest.Provider), provider.ParamName);
        Assert.Equal(nameof(TranslationProviderRequest.Model), model.ParamName);
    }

    [Fact]
    public void PrintableRepresentation_RedactsEndpointCredentialAndQueryText()
    {
        const string apiKey = "APIKEY_CANARY_PROVIDER_REQUEST";
        const string query = "QUERY_CANARY_PROVIDER_REQUEST";
        var endpoint = new Uri("https://provider-canary.example.test/private-path");
        var request = new TranslationProviderRequest(
            new ProviderDescriptor(TranslationProviderIds.OpenAiCompatible),
            endpoint,
            "model-canary",
            query,
            "en-US",
            "zh-Hans",
            new TranslationCredentials(apiKey),
            TimeSpan.FromSeconds(30));

        var printable = request.ToString();

        Assert.False(printable.Contains(apiKey, StringComparison.Ordinal));
        Assert.False(printable.Contains(query, StringComparison.Ordinal));
        Assert.False(printable.Contains(endpoint.AbsoluteUri, StringComparison.Ordinal));
        Assert.Contains("ProviderId=openai-compatible", printable);
    }

    private static TranslationProviderRequest CreateRequest(string providerId) => new(
        new ProviderDescriptor(providerId),
        new Uri("https://provider.example.test/translate"),
        "test-model",
        "synthetic source text",
        "en-US",
        "zh-Hans",
        new TranslationCredentials("APIKEY_CANARY_CORE_REQUEST"),
        TimeSpan.FromSeconds(30));
}
