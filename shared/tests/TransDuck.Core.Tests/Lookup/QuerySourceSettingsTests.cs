// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;

namespace TransDuck.Core.Tests.Lookup;

public sealed class QuerySourceSettingsTests
{
    [Fact]
    public void Validate_AllowsMultipleProvidersAndDictionaryOnlyConfigurations()
    {
        var combined = new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            [new ProviderDescriptor("deepl"), new ProviderDescriptor("ollama", "local")],
            new LocalDictionarySettings(true, "/data/dictionary.csv"),
            MacSystemDictionaryEnabled: true);
        var dictionaryOnly = new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            [],
            new LocalDictionarySettings(true, "/data/dictionary.db"),
            MacSystemDictionaryEnabled: false);
        var macSystemOnly = new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            [],
            LocalDictionarySettings.Disabled,
            MacSystemDictionaryEnabled: true);

        combined.Validate();
        dictionaryOnly.Validate();
        macSystemOnly.Validate();

        Assert.Equal(2, combined.EnabledTranslationProviders.Count);
        Assert.True(dictionaryOnly.LocalDictionary.Enabled);
        Assert.True(macSystemOnly.MacSystemDictionaryEnabled);
    }

    [Fact]
    public void Validate_RejectsDuplicateOrEmptySourceSelections()
    {
        var duplicate = new QuerySourceSettings(
            1,
            [new ProviderDescriptor("deepl"), new ProviderDescriptor("deepl")],
            LocalDictionarySettings.Disabled,
            false);
        var empty = new QuerySourceSettings(1, [], LocalDictionarySettings.Disabled, false);
        var missingPath = new QuerySourceSettings(
            1,
            [],
            new LocalDictionarySettings(true, null),
            false);

        Assert.Throws<ContractValidationException>(duplicate.Validate);
        Assert.Throws<ContractValidationException>(empty.Validate);
        Assert.Throws<ContractValidationException>(missingPath.Validate);
    }

    [Fact]
    public void ToString_DoesNotExposeDictionaryPath()
    {
        var path = "/private/QUERY_SOURCE_PATH_CANARY/dictionary.csv";
        var settings = new QuerySourceSettings(
            1,
            [],
            new LocalDictionarySettings(true, path),
            false);

        var text = settings.ToString() + settings.LocalDictionary;

        Assert.DoesNotContain(path, text, StringComparison.Ordinal);
        Assert.Contains("HasDataFilePath=True", text, StringComparison.Ordinal);
    }
}
