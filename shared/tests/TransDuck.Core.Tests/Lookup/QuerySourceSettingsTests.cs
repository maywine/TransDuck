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
            new EcdictDictionarySettings(true, "/data/ecdict.csv"),
            MacSystemDictionaryEnabled: true);
        var dictionaryOnly = new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            [],
            new EcdictDictionarySettings(true, "/data/ecdict.db"),
            MacSystemDictionaryEnabled: false);
        var macSystemOnly = new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            [],
            EcdictDictionarySettings.Disabled,
            MacSystemDictionaryEnabled: true);

        combined.Validate();
        dictionaryOnly.Validate();
        macSystemOnly.Validate();

        Assert.Equal(2, combined.EnabledTranslationProviders.Count);
        Assert.True(dictionaryOnly.Ecdict.Enabled);
        Assert.True(macSystemOnly.MacSystemDictionaryEnabled);
    }

    [Fact]
    public void Validate_RejectsDuplicateOrEmptySourceSelections()
    {
        var duplicate = new QuerySourceSettings(
            1,
            [new ProviderDescriptor("deepl"), new ProviderDescriptor("deepl")],
            EcdictDictionarySettings.Disabled,
            false);
        var empty = new QuerySourceSettings(1, [], EcdictDictionarySettings.Disabled, false);
        var missingPath = new QuerySourceSettings(
            1,
            [],
            new EcdictDictionarySettings(true, null),
            false);

        Assert.Throws<ContractValidationException>(duplicate.Validate);
        Assert.Throws<ContractValidationException>(empty.Validate);
        Assert.Throws<ContractValidationException>(missingPath.Validate);
    }

    [Fact]
    public void ToString_DoesNotExposeDictionaryPath()
    {
        var path = "/private/QUERY_SOURCE_PATH_CANARY/ecdict.csv";
        var settings = new QuerySourceSettings(
            1,
            [],
            new EcdictDictionarySettings(true, path),
            false);

        var text = settings.ToString() + settings.Ecdict;

        Assert.DoesNotContain(path, text, StringComparison.Ordinal);
        Assert.Contains("HasDataFilePath=True", text, StringComparison.Ordinal);
    }
}
