// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Core.Contracts.V1;

/// <summary>
/// Categorizes validation failures that can be surfaced without provider details.
/// </summary>
public enum ContractValidationError
{
    MissingRequired,
    UnsupportedSchemaVersion,
    InvalidValue,
    InvalidTerminalShape,
}

/// <summary>
/// Describes a contract validation failure using a stable category.
/// </summary>
public sealed class ContractValidationException : ArgumentException
{
    public ContractValidationException(
        ContractValidationError error,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    public ContractValidationError Error { get; }
}

/// <summary>
/// Holds shared v1 validation rules so schema-required fields are checked consistently.
/// </summary>
internal static partial class ContractValidation
{
    public const int SchemaVersion = 1;

    private static readonly Regex IdentifierPattern = IdentifierRegex();
    private static readonly Regex LanguagePattern = LanguageRegex();
    private static readonly Regex ProviderIdPattern = ProviderIdRegex();
    private static readonly Regex InstanceIdPattern = InstanceIdRegex();

    public static void RequireSchemaVersion(int schemaVersion)
    {
        if (schemaVersion != SchemaVersion)
        {
            throw new ContractValidationException(
                ContractValidationError.UnsupportedSchemaVersion,
                $"Unsupported schemaVersion: {schemaVersion}.");
        }
    }

    public static void RequireIdentifier(string? value, string propertyName)
    {
        RequireString(value, propertyName);
        if (!IdentifierPattern.IsMatch(value!))
        {
            ThrowInvalid(propertyName);
        }
    }

    public static void RequireLanguage(string? value, string propertyName)
    {
        RequireString(value, propertyName);
        if (!LanguagePattern.IsMatch(value!))
        {
            ThrowInvalid(propertyName);
        }
    }

    public static void RequireOptionalLanguage(string? value, string propertyName)
    {
        if (value is not null)
        {
            RequireLanguage(value, propertyName);
        }
    }

    public static void RequireProviderId(string? value)
    {
        RequireString(value, "providerId");
        if (!ProviderIdPattern.IsMatch(value!))
        {
            ThrowInvalid("providerId");
        }
    }

    public static void RequireOptionalInstanceId(string? value)
    {
        if (value is not null && !InstanceIdPattern.IsMatch(value))
        {
            ThrowInvalid("instanceId");
        }
    }

    public static void RequireString(string? value, string propertyName)
    {
        if (value is null)
        {
            throw new ContractValidationException(
                ContractValidationError.MissingRequired,
                $"Missing required property: {propertyName}.");
        }

        if (value.Length == 0)
        {
            ThrowInvalid(propertyName);
        }
    }

    public static void RequireCondition(
        bool condition,
        ContractValidationError error,
        string message)
    {
        if (!condition)
        {
            throw new ContractValidationException(error, message);
        }
    }

    private static void ThrowInvalid(string propertyName) =>
        throw new ContractValidationException(
            ContractValidationError.InvalidValue,
            $"Invalid property value: {propertyName}.");

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageRegex();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderIdRegex();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceIdRegex();
}
