// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Translation;

/// <summary>
/// Defines non-secret settings for one configured translation provider profile.
/// </summary>
public sealed record ProviderProfileSettings(
    [property: JsonRequired] ProviderDescriptor Provider,
    [property: JsonRequired] Uri Endpoint,
    string? Model,
    string? SourceLanguage,
    [property: JsonRequired] string TargetLanguage,
    [property: JsonRequired] int TimeoutSeconds)
{
    /// <summary>Gets a stable provider and optional instance key used to detect duplicate profiles.</summary>
    public string CanonicalProviderKey => Provider.InstanceId is null
        ? Provider.ProviderId
        : Provider.ProviderId + ":" + Provider.InstanceId;

    /// <summary>Validates non-secret provider settings shared by all supported adapters.</summary>
    public void Validate()
    {
        ContractValidation.RequireCondition(
            Provider is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: provider.");
        Provider!.Validate();
        ContractValidation.RequireCondition(
            Endpoint is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: endpoint.");
        if (!Endpoint!.IsAbsoluteUri || Endpoint.Scheme is not ("https" or "http"))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Provider endpoint must be an absolute HTTP(S) URI.");
        }

        // Endpoint is a non-secret base/path setting; credential material belongs in the DPAPI store.
        if (!string.IsNullOrEmpty(Endpoint.UserInfo) || !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Provider endpoint cannot contain user info, query, or fragment.");
        }

        if (Model is not null && string.IsNullOrWhiteSpace(Model))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Optional model cannot be empty when supplied.");
        }

        ContractValidation.RequireOptionalLanguage(SourceLanguage, "sourceLanguage");
        ContractValidation.RequireLanguage(TargetLanguage, "targetLanguage");
        ContractValidation.RequireCondition(
            TimeoutSeconds is >= 1 and <= 600,
            ContractValidationError.InvalidValue,
            "timeoutSeconds must be between 1 and 600.");
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"ProviderProfileSettings(ProviderId={Provider?.ProviderId ?? "<none>"}, " +
        $"HasInstance={Provider?.InstanceId is not null}, HasModel={Model is not null}, " +
        $"SourceLanguage={SourceLanguage ?? "<none>"}, TargetLanguage={TargetLanguage ?? "<none>"}, " +
        $"TimeoutSeconds={TimeoutSeconds})";
}

/// <summary>
/// Represents versioned non-secret provider profiles and permits an empty profile list.
/// </summary>
public sealed record ProviderSettingsDocument(
    [property: JsonRequired] int Version,
    [property: JsonRequired] IReadOnlyList<ProviderProfileSettings> Profiles)
{
    /// <summary>Validates version presence and profile uniqueness by canonical provider and instance key.</summary>
    public void Validate()
    {
        ContractValidation.RequireCondition(
            Version >= 1,
            ContractValidationError.InvalidValue,
            "version must be positive.");
        ContractValidation.RequireCondition(
            Profiles is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: profiles.");

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in Profiles!)
        {
            ContractValidation.RequireCondition(
                profile is not null,
                ContractValidationError.InvalidValue,
                "profiles cannot contain null values.");
            profile!.Validate();
            ContractValidation.RequireCondition(
                keys.Add(profile.CanonicalProviderKey),
                ContractValidationError.InvalidValue,
                "Provider profiles must be unique by provider and instance.");
        }
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"ProviderSettingsDocument(Version={Version}, ProfileCount={Profiles?.Count ?? 0})";
}
