// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;

namespace TransDuck.Core.Tests.Translation;

public sealed class ProviderProfileSettingsTests
{
    [Fact]
    public void Validate_AllowsCanonicalProviderInstanceAndOptionalSettings()
    {
        var profile = Profile("openai-compatible", "profile-a") with
        {
            Model = null,
            SourceLanguage = null,
        };

        profile.Validate();

        Assert.Equal("openai-compatible:profile-a", profile.CanonicalProviderKey);
    }

    [Theory]
    [InlineData("ftp://provider.example.test/translate")]
    [InlineData("relative/path")]
    [InlineData("https://user:APIKEY_CANARY@provider.example.test/translate")]
    [InlineData("https://provider.example.test/translate?api_key=APIKEY_CANARY")]
    [InlineData("https://provider.example.test/translate#APIKEY_CANARY")]
    public void Validate_RejectsNonHttpEndpoint(string endpoint)
    {
        var profile = Profile("deepl") with { Endpoint = new Uri(endpoint, UriKind.RelativeOrAbsolute) };

        var exception = Assert.Throws<ContractValidationException>(profile.Validate);

        Assert.Equal(ContractValidationError.InvalidValue, exception.Error);
    }

    [Fact]
    public void Validate_RejectsEmptyModelInvalidLanguagesAndTimeout()
    {
        var emptyModel = Profile("ollama") with { Model = " " };
        var language = Profile("ollama") with { TargetLanguage = "not a language" };
        var timeout = Profile("ollama") with { TimeoutSeconds = 0 };

        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(emptyModel.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(language.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(timeout.Validate).Error);
    }

    [Fact]
    public void SettingsDocument_RequiresUniqueCanonicalProviderAndInstance()
    {
        var first = Profile("openai-compatible", "profile-a");
        var duplicate = Profile("openai-compatible", "profile-a") with { Endpoint = new Uri("https://other.example.test/v1") };
        var document = new ProviderSettingsDocument(ProviderSettingsMigration.CurrentVersion, [first, duplicate]);

        var exception = Assert.Throws<ContractValidationException>(document.Validate);

        Assert.Equal(ContractValidationError.InvalidValue, exception.Error);
    }

    [Fact]
    public void PrintableRepresentations_RedactEndpointAndDoNotExposeSecretLikeFields()
    {
        var endpoint = new Uri("https://endpoint-canary.example.test/private-settings");
        var profile = Profile("openai-compatible", "profile-a") with
        {
            Endpoint = endpoint,
            Model = "MODEL_CANARY_SETTINGS",
        };
        var document = new ProviderSettingsDocument(ProviderSettingsMigration.CurrentVersion, [profile]);

        var profileText = profile.ToString();
        var documentText = document.ToString();
        var propertyNames = typeof(ProviderProfileSettings).GetProperties().Select(property => property.Name);

        Assert.False(profileText.Contains(endpoint.AbsoluteUri, StringComparison.Ordinal));
        Assert.False(profileText.Contains("MODEL_CANARY_SETTINGS", StringComparison.Ordinal));
        Assert.False(documentText.Contains(endpoint.AbsoluteUri, StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("apikey", StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderProfileSettings Profile(string providerId, string? instanceId = null) => new(
        new ProviderDescriptor(providerId, instanceId),
        new Uri("https://provider.example.test/translate"),
        "test-model",
        "en-US",
        "zh-Hans",
        30);
}
