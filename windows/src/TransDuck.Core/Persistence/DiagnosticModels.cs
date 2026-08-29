// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Persistence;

/// <summary>
/// Describes the severity of a structured diagnostic event.
/// </summary>
public enum DiagnosticLevel
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// Defines the only event identifiers accepted by the diagnostic persistence boundary.
/// </summary>
public enum DiagnosticEventId
{
    ConfigurationRead,
    ConfigurationWrite,
    CredentialRead,
    CredentialWrite,
    CredentialRemove,
    HistoryRead,
    HistoryAppend,
    HistoryClear,
    DiagnosticWrite,
    ProviderSettingsRead,
    ProviderSettingsWrite,
    HotkeySettingsRead,
    HotkeySettingsWrite,
    HotkeyRegistration,
    StartupRegistration,
    ProxySettingsRead,
    ProxySettingsWrite,
    TranslationStarted,
    TranslationCompleted,
    TranslationFailed,
    TranslationCancelled,
}

/// <summary>
/// Describes the closed outcome set accepted by the diagnostic sink.
/// </summary>
public enum DiagnosticOutcome
{
    Succeeded,
    Cancelled,
    Failed,
    NotFound,
}

/// <summary>
/// Describes a closed non-secret diagnostic error category.
/// </summary>
public enum DiagnosticErrorCode
{
    InvalidData,
    UnsupportedVersion,
    CorruptData,
    IoFailure,
    TranslationInvalidRequest,
    TranslationProviderUnavailable,
    TranslationTimeout,
    TranslationNetwork,
    TranslationAuthentication,
    TranslationRateLimited,
    TranslationUnsupportedLanguage,
    TranslationInternal,
    HotkeyConflict,
    HotkeyRegistrationFailure,
    StartupRegistrationFailure,
}

/// <summary>
/// Contains only structured, non-secret diagnostic fields allowed to reach persistence.
/// </summary>
public sealed record DiagnosticEvent(
    DateTimeOffset Timestamp,
    DiagnosticLevel Level,
    DiagnosticEventId EventId,
    DiagnosticOutcome Outcome,
    string? RequestId = null,
    string? ProviderId = null,
    DiagnosticErrorCode? ErrorCode = null,
    long? DurationMs = null)
{
    /// <summary>Validates the closed event shape before it is written.</summary>
    public void Validate()
    {
        if (Timestamp == default)
        {
            throw new ContractValidationException(
                ContractValidationError.MissingRequired,
                "Missing required property: timestamp.");
        }

        if (!Enum.IsDefined(Level) || !Enum.IsDefined(EventId) || !Enum.IsDefined(Outcome))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Invalid diagnostic level, eventId, or outcome.");
        }

        if (RequestId is not null)
        {
            ContractValidation.RequireIdentifier(RequestId, "requestId");
        }

        if (ProviderId is not null)
        {
            new ProviderDescriptor(ProviderId).Validate();
        }

        if (ErrorCode is { } errorCode && !Enum.IsDefined(errorCode))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Invalid diagnostic errorCode.");
        }

        if (DurationMs is < 0)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "durationMs must be non-negative.");
        }
    }
}
