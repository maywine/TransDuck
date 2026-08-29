// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;

namespace TransDuck.Core.Contracts.V1;

public enum StreamEventType
{
    Delta,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>
/// Defines one ordered stream event whose terminal shape is validated locally.
/// </summary>
public sealed record StreamEvent(
    [property: JsonRequired] int SchemaVersion,
    [property: JsonRequired] string RequestId,
    [property: JsonRequired] long Sequence,
    [property: JsonRequired] StreamEventType EventType,
    string? Text = null,
    QueryError? Error = null) : IContractDocument
{
    public bool IsTerminal => EventType is not StreamEventType.Delta;

    public void Validate()
    {
        ContractValidation.RequireSchemaVersion(SchemaVersion);
        ContractValidation.RequireIdentifier(RequestId, "requestId");
        ContractValidation.RequireCondition(
            Sequence >= 0,
            ContractValidationError.InvalidValue,
            "sequence must be non-negative.");
        ContractValidation.RequireCondition(
            Enum.IsDefined(EventType),
            ContractValidationError.InvalidValue,
            "Invalid eventType.");

        switch (EventType)
        {
            case StreamEventType.Delta:
                ContractValidation.RequireCondition(
                    Text is not null && Error is null,
                    ContractValidationError.InvalidTerminalShape,
                    "Delta events require text and cannot carry error.");
                break;
            case StreamEventType.Completed:
            case StreamEventType.Cancelled:
                ContractValidation.RequireCondition(
                    Text is null && Error is null,
                    ContractValidationError.InvalidTerminalShape,
                    "Completed and cancelled events cannot carry text or error.");
                break;
            case StreamEventType.Failed:
                ContractValidation.RequireCondition(
                    Text is null && Error is not null,
                    ContractValidationError.InvalidTerminalShape,
                    "Failed events require error and cannot carry text.");
                Error!.Validate();
                break;
            default:
                throw new ContractValidationException(
                    ContractValidationError.InvalidValue,
                    "Invalid eventType.");
        }
    }
}
