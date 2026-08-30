// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.Core.Tests.Persistence;

public sealed class CorePersistenceModelTests
{
    [Fact]
    public void CredentialKey_ValidatesStableProviderAndInstanceIdentifiers()
    {
        var key = new CredentialKey("openai-compatible", "profile-a");

        key.Validate();

        Assert.Equal("openai-compatible:profile-a", key.CanonicalValue);
        Assert.Throws<ContractValidationException>(() =>
            new CredentialKey("Invalid Provider").Validate());
    }

    [Fact]
    public void CredentialSecret_RedactsAndRejectsAccessAfterDisposal()
    {
        const string canary = "APIKEY_CANARY_CORE_PERSISTENCE";
        using var secret = new CredentialSecret(canary);

        Assert.Equal(canary, secret.Reveal());
        Assert.Equal(canary, Encoding.UTF8.GetString(secret.ExportUtf8()));
        Assert.False(secret.ToString().Contains(canary, StringComparison.Ordinal));

        secret.Dispose();

        Assert.Throws<ObjectDisposedException>(secret.Reveal);
        Assert.Throws<ObjectDisposedException>(secret.ExportUtf8);
        Assert.False(secret.ToString().Contains(canary, StringComparison.Ordinal));
    }

    [Fact]
    public void CredentialSecret_RejectsEmptyValuesAndInvalidUtf8()
    {
        Assert.Throws<ArgumentException>(() => new CredentialSecret(string.Empty));
        Assert.Throws<DecoderFallbackException>(() => CredentialSecret.FromUtf8([0xFF]));
    }

    [Fact]
    public void PersistenceResults_ExposeStableSuccessAndValueSemantics()
    {
        using var secret = new CredentialSecret("APIKEY_CANARY_RESULT");
        var success = PersistenceResult.Success();
        var notFound = PersistenceResult.FromStatus(PersistenceStatus.NotFound);
        var readSuccess = PersistenceReadResult<CredentialSecret>.Success(secret);
        var cancelled = PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.Cancelled);

        Assert.True(success.Succeeded);
        Assert.False(notFound.Succeeded);
        Assert.True(readSuccess.Succeeded);
        Assert.Same(secret, readSuccess.Value);
        Assert.False(cancelled.Succeeded);
        Assert.Null(cancelled.Value);
    }

    [Fact]
    public void DiagnosticEvent_ExposesOnlyClosedStructuredFields()
    {
        var properties = typeof(DiagnosticEvent).GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var expected = new[]
        {
            "DurationMs",
            "ErrorCode",
            "EventId",
            "Level",
            "Outcome",
            "ProviderId",
            "RequestId",
            "Timestamp",
        };

        Assert.Equal(expected, properties);
        Assert.Equal(typeof(DiagnosticEventId), typeof(DiagnosticEvent).GetProperty("EventId")!.PropertyType);
        Assert.DoesNotContain(properties, name =>
            name.Contains("message", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("query", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("clipboard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticEvent_RejectsInvalidStructuredIdentifiers()
    {
        var diagnostic = new DiagnosticEvent(
            DateTimeOffset.UtcNow,
            DiagnosticLevel.Information,
            DiagnosticEventId.HistoryRead,
            DiagnosticOutcome.Succeeded,
            RequestId: "QUERY CANARY");

        var exception = Assert.Throws<ContractValidationException>(diagnostic.Validate);

        Assert.Equal(ContractValidationError.InvalidValue, exception.Error);
    }

    [Fact]
    public void DiagnosticEnums_RemainClosedAndRejectUndefinedValues()
    {
        var expectedLevels = new[]
        {
            DiagnosticLevel.Information,
            DiagnosticLevel.Warning,
            DiagnosticLevel.Error,
        };
        var expected = new[]
        {
            DiagnosticEventId.ConfigurationRead,
            DiagnosticEventId.ConfigurationWrite,
            DiagnosticEventId.CredentialRead,
            DiagnosticEventId.CredentialWrite,
            DiagnosticEventId.CredentialRemove,
            DiagnosticEventId.HistoryRead,
            DiagnosticEventId.HistoryAppend,
            DiagnosticEventId.HistoryClear,
            DiagnosticEventId.DiagnosticWrite,
            DiagnosticEventId.ProviderSettingsRead,
            DiagnosticEventId.ProviderSettingsWrite,
            DiagnosticEventId.HotkeySettingsRead,
            DiagnosticEventId.HotkeySettingsWrite,
            DiagnosticEventId.HotkeyRegistration,
            DiagnosticEventId.StartupRegistration,
            DiagnosticEventId.ProxySettingsRead,
            DiagnosticEventId.ProxySettingsWrite,
            DiagnosticEventId.TranslationStarted,
            DiagnosticEventId.TranslationCompleted,
            DiagnosticEventId.TranslationFailed,
            DiagnosticEventId.TranslationCancelled,
        };
        var expectedOutcomes = new[]
        {
            DiagnosticOutcome.Succeeded,
            DiagnosticOutcome.Cancelled,
            DiagnosticOutcome.Failed,
            DiagnosticOutcome.NotFound,
        };
        var timestamp = DateTimeOffset.UtcNow;
        var invalidLevel = new DiagnosticEvent(
            timestamp,
            (DiagnosticLevel)999,
            DiagnosticEventId.HistoryRead,
            DiagnosticOutcome.Succeeded);
        var invalid = new DiagnosticEvent(
            timestamp,
            DiagnosticLevel.Information,
            (DiagnosticEventId)999,
            DiagnosticOutcome.Succeeded);
        var invalidOutcome = new DiagnosticEvent(
            timestamp,
            DiagnosticLevel.Information,
            DiagnosticEventId.HistoryRead,
            (DiagnosticOutcome)999);
        var invalidError = new DiagnosticEvent(
            timestamp,
            DiagnosticLevel.Error,
            DiagnosticEventId.TranslationFailed,
            DiagnosticOutcome.Failed,
            ErrorCode: (DiagnosticErrorCode)999);
        var expectedErrorCodes = new[]
        {
            DiagnosticErrorCode.InvalidData,
            DiagnosticErrorCode.UnsupportedVersion,
            DiagnosticErrorCode.CorruptData,
            DiagnosticErrorCode.IoFailure,
            DiagnosticErrorCode.TranslationInvalidRequest,
            DiagnosticErrorCode.TranslationProviderUnavailable,
            DiagnosticErrorCode.TranslationTimeout,
            DiagnosticErrorCode.TranslationNetwork,
            DiagnosticErrorCode.TranslationAuthentication,
            DiagnosticErrorCode.TranslationRateLimited,
            DiagnosticErrorCode.TranslationUnsupportedLanguage,
            DiagnosticErrorCode.TranslationInternal,
            DiagnosticErrorCode.HotkeyConflict,
            DiagnosticErrorCode.HotkeyRegistrationFailure,
            DiagnosticErrorCode.StartupRegistrationFailure,
        };

        Assert.Equal(expectedLevels, Enum.GetValues<DiagnosticLevel>());
        Assert.Equal(expected, Enum.GetValues<DiagnosticEventId>());
        Assert.Equal(expectedOutcomes, Enum.GetValues<DiagnosticOutcome>());
        Assert.Equal(expectedErrorCodes, Enum.GetValues<DiagnosticErrorCode>());
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(invalidLevel.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(invalid.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(invalidOutcome.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(invalidError.Validate).Error);
    }
}
