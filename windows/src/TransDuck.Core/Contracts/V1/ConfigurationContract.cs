// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;

namespace TransDuck.Core.Contracts.V1;

/// <summary>
/// Defines retention policy values without implementing history persistence.
/// </summary>
public sealed record HistoryRetention(
    [property: JsonRequired] int MaxEntries,
    [property: JsonRequired] int MaxAgeDays)
{
    public void Validate()
    {
        ContractValidation.RequireCondition(
            MaxEntries is >= 0 and <= 10000,
            ContractValidationError.InvalidValue,
            "maxEntries must be between 0 and 10000.");
        ContractValidation.RequireCondition(
            MaxAgeDays is >= 0 and <= 3650,
            ContractValidationError.InvalidValue,
            "maxAgeDays must be between 0 and 3650.");
    }
}

/// <summary>
/// Carries versioned non-secret configuration shared across platform clients.
/// </summary>
public sealed record Configuration(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] int Version,
    [property: JsonRequired] ProviderDescriptor DefaultProvider,
    [property: JsonRequired] HistoryRetention HistoryRetention) : IContractDocument
{
    public void Validate()
    {
        ContractValidation.RequireSchemaVersion(SchemaVersion);
        ContractValidation.RequireCondition(
            Version >= 1,
            ContractValidationError.InvalidValue,
            "version must be positive.");
        ContractValidation.RequireCondition(
            DefaultProvider is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: defaultProvider.");
        DefaultProvider!.Validate();
        ContractValidation.RequireCondition(
            HistoryRetention is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: historyRetention.");
        HistoryRetention!.Validate();
    }
}
