// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Lookup;

/// <summary>
/// Defines the persisted query-source settings version supported by this client.
/// </summary>
public static class QuerySourceSettingsMigration
{
    public const int CurrentVersion = 1;
}

/// <summary>
/// Selects an optional user-owned local dictionary file.
/// </summary>
public sealed record LocalDictionarySettings(
    [property: JsonRequired] bool Enabled,
    string? DataFilePath)
{
    public static LocalDictionarySettings Disabled { get; } = new(false, null);

    public void Validate()
    {
        if (DataFilePath is not null &&
            (string.IsNullOrWhiteSpace(DataFilePath) || DataFilePath.Length > 32768 || DataFilePath.Contains('\0')))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Local dictionary data file path is invalid.");
        }

        if (Enabled && DataFilePath is null)
        {
            throw new ContractValidationException(
                ContractValidationError.MissingRequired,
                "An enabled local dictionary requires a data file path.");
        }
    }

    public override string ToString() =>
        $"LocalDictionarySettings(Enabled={Enabled}, HasDataFilePath={DataFilePath is not null})";
}

/// <summary>
/// Stores the translation providers and local dictionaries queried for each input.
/// </summary>
public sealed record QuerySourceSettings(
    [property: JsonRequired] int Version,
    [property: JsonRequired] IReadOnlyList<ProviderDescriptor> EnabledTranslationProviders,
    [property: JsonRequired, JsonPropertyName("localDictionary")] LocalDictionarySettings LocalDictionary,
    [property: JsonRequired] bool MacSystemDictionaryEnabled)
{
    public static QuerySourceSettings CreateDefault(ProviderDescriptor provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            [provider],
            LocalDictionarySettings.Disabled,
            MacSystemDictionaryEnabled: false);
    }

    public void Validate()
    {
        if (Version != QuerySourceSettingsMigration.CurrentVersion)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Query source settings version is not supported.");
        }

        ContractValidation.RequireCondition(
            EnabledTranslationProviders is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: enabledTranslationProviders.");
        ContractValidation.RequireCondition(
            EnabledTranslationProviders!.Count <= 32,
            ContractValidationError.InvalidValue,
            "Too many translation providers are enabled.");
        ContractValidation.RequireCondition(
            LocalDictionary is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: localDictionary.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in EnabledTranslationProviders)
        {
            ContractValidation.RequireCondition(
                provider is not null,
                ContractValidationError.InvalidValue,
                "enabledTranslationProviders cannot contain null values.");
            provider!.Validate();
            var key = provider.InstanceId is null
                ? provider.ProviderId
                : provider.ProviderId + ":" + provider.InstanceId;
            ContractValidation.RequireCondition(
                keys.Add(key),
                ContractValidationError.InvalidValue,
                "Enabled translation providers must be unique by provider and instance.");
        }

        LocalDictionary!.Validate();
        ContractValidation.RequireCondition(
            EnabledTranslationProviders.Count > 0 || LocalDictionary.Enabled || MacSystemDictionaryEnabled,
            ContractValidationError.InvalidValue,
            "At least one translation or dictionary source must be enabled.");
    }

    public override string ToString() =>
        $"QuerySourceSettings(Version={Version}, " +
        $"TranslationProviderCount={EnabledTranslationProviders?.Count ?? 0}, " +
        $"LocalDictionaryEnabled={LocalDictionary?.Enabled ?? false}, " +
        $"MacSystemDictionaryEnabled={MacSystemDictionaryEnabled})";
}
