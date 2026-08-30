// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;

namespace TransDuck.Core.Contracts.V1;

public enum QueryKind
{
    Translation,
    Dictionary,
    Ocr,
}

public enum QueryTerminalState
{
    Completed,
    Cancelled,
    Failed,
}

public enum QueryErrorCode
{
    InvalidRequest,
    ProviderUnavailable,
    Timeout,
    Network,
    Authentication,
    RateLimited,
    UnsupportedLanguage,
    Internal,
}

/// <summary>
/// Identifies a provider kind and, when needed, a separate configured instance.
/// </summary>
public sealed record ProviderDescriptor(
    [property: JsonRequired] string ProviderId,
    string? InstanceId = null)
{
    public void Validate()
    {
        ContractValidation.RequireProviderId(ProviderId);
        ContractValidation.RequireOptionalInstanceId(InstanceId);
    }
}

/// <summary>
/// Carries a safe, provider-neutral error terminal for a query or stream event.
/// </summary>
public sealed record QueryError(
    [property: JsonRequired] QueryErrorCode Code,
    [property: JsonRequired] string Message,
    [property: JsonRequired] bool Retryable)
{
    public void Validate()
    {
        ContractValidation.RequireCondition(
            Enum.IsDefined(Code),
            ContractValidationError.InvalidValue,
            "Invalid query error code.");
        ContractValidation.RequireString(Message, "message");
    }
}

public sealed record DictionaryEntryResult(
    [property: JsonRequired] string Term,
    [property: JsonRequired] IReadOnlyList<string> Definitions)
{
    public void Validate()
    {
        ContractValidation.RequireString(Term, "term");
        ContractValidation.RequireCondition(
            Definitions is { Count: > 0 },
            ContractValidationError.MissingRequired,
            "Missing required property: definitions.");
        foreach (var definition in Definitions)
        {
            ContractValidation.RequireString(definition, "definitions");
        }
    }
}

public sealed record QueryResultPayload(
    [property: JsonRequired] string Text,
    IReadOnlyList<DictionaryEntryResult>? DictionaryEntries = null)
{
    public void Validate()
    {
        ContractValidation.RequireString(Text, "text");

        if (DictionaryEntries is null)
        {
            return;
        }

        foreach (var entry in DictionaryEntries)
        {
            ContractValidation.RequireCondition(
                entry is not null,
                ContractValidationError.InvalidValue,
                "dictionaryEntries cannot contain null values.");
            entry!.Validate();
        }
    }
}

/// <summary>
/// Describes one provider-neutral query without endpoint or credential fields.
/// </summary>
public sealed record QueryRequest(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string RequestId,
    [property: JsonRequired] QueryKind QueryKind,
    [property: JsonRequired] string Text,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SourceLanguage,
    [property: JsonRequired] string TargetLanguage,
    [property: JsonRequired] ProviderDescriptor Provider) : IContractDocument
{
    public void Validate()
    {
        ContractValidation.RequireSchemaVersion(SchemaVersion);
        ContractValidation.RequireIdentifier(RequestId, "requestId");
        ContractValidation.RequireCondition(
            Enum.IsDefined(QueryKind),
            ContractValidationError.InvalidValue,
            "Invalid queryKind.");
        ContractValidation.RequireString(Text, "text");
        ContractValidation.RequireOptionalLanguage(SourceLanguage, "sourceLanguage");
        ContractValidation.RequireLanguage(TargetLanguage, "targetLanguage");
        ContractValidation.RequireCondition(
            Provider is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: provider.");
        Provider!.Validate();
    }
}

/// <summary>
/// Represents one immutable query terminal with a shape determined by TerminalState.
/// </summary>
public sealed record QueryResult(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string RequestId,
    [property: JsonRequired] QueryKind QueryKind,
    [property: JsonRequired] ProviderDescriptor Provider,
    [property: JsonRequired] QueryTerminalState TerminalState,
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SourceLanguage,
    [property: JsonRequired] string TargetLanguage,
    QueryResultPayload? Result = null,
    QueryError? Error = null) : IContractDocument
{
    public void Validate()
    {
        ContractValidation.RequireSchemaVersion(SchemaVersion);
        ContractValidation.RequireIdentifier(RequestId, "requestId");
        ContractValidation.RequireCondition(
            Enum.IsDefined(QueryKind),
            ContractValidationError.InvalidValue,
            "Invalid queryKind.");
        ContractValidation.RequireCondition(
            Enum.IsDefined(TerminalState),
            ContractValidationError.InvalidValue,
            "Invalid terminalState.");
        ContractValidation.RequireOptionalLanguage(SourceLanguage, "sourceLanguage");
        ContractValidation.RequireLanguage(TargetLanguage, "targetLanguage");
        ContractValidation.RequireCondition(
            Provider is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: provider.");
        Provider!.Validate();

        switch (TerminalState)
        {
            case QueryTerminalState.Completed:
                ContractValidation.RequireCondition(
                    Result is not null && Error is null,
                    ContractValidationError.InvalidTerminalShape,
                    "Completed queries require result and cannot carry error.");
                Result!.Validate();
                break;
            case QueryTerminalState.Cancelled:
                ContractValidation.RequireCondition(
                    Result is null && Error is null,
                    ContractValidationError.InvalidTerminalShape,
                    "Cancelled queries cannot carry result or error.");
                break;
            case QueryTerminalState.Failed:
                ContractValidation.RequireCondition(
                    Result is null && Error is not null,
                    ContractValidationError.InvalidTerminalShape,
                    "Failed queries require error and cannot carry result.");
                Error!.Validate();
                break;
            default:
                throw new ContractValidationException(
                    ContractValidationError.InvalidValue,
                    "Invalid terminalState.");
        }
    }
}
