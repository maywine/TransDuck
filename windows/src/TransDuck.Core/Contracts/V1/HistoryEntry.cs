// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;

namespace TransDuck.Core.Contracts.V1;

/// <summary>
/// Represents a serializable history record without defining its storage mechanism.
/// </summary>
public sealed record HistoryEntry(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string EntryId,
    [property: JsonRequired] DateTimeOffset CreatedAt,
    [property: JsonRequired] QueryRequest Request,
    [property: JsonRequired] QueryResult Result) : IContractDocument
{
    public void Validate()
    {
        ContractValidation.RequireSchemaVersion(SchemaVersion);
        ContractValidation.RequireIdentifier(EntryId, "entryId");
        ContractValidation.RequireCondition(
            Request is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: request.");
        ContractValidation.RequireCondition(
            Result is not null,
            ContractValidationError.MissingRequired,
            "Missing required property: result.");
        Request!.Validate();
        Result!.Validate();
        ContractValidation.RequireCondition(
            string.Equals(Request.RequestId, Result.RequestId, StringComparison.Ordinal),
            ContractValidationError.InvalidValue,
            "History requestId and result requestId must match.");
        ContractValidation.RequireCondition(
            Request.QueryKind == Result.QueryKind,
            ContractValidationError.InvalidValue,
            "History request queryKind and result queryKind must match.");
        ContractValidation.RequireCondition(
            Equals(Request.Provider, Result.Provider),
            ContractValidationError.InvalidValue,
            "History request provider and result provider must match.");
    }
}
