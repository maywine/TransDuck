// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;

namespace TransDuck.App.Services;

/// <summary>
/// Coordinates non-secret provider settings, credentials, and configuration without exposing storage to WPF code-behind.
/// </summary>
internal sealed class ProviderSettingsController
{
    private static readonly HistoryRetention DefaultRetention = new(100, 30);
    private readonly IProviderSettingsStore _providerSettingsStore;
    private readonly IConfigurationStore _configurationStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IDiagnosticSink _diagnosticSink;

    public ProviderSettingsController(
        IProviderSettingsStore providerSettingsStore,
        IConfigurationStore configurationStore,
        ICredentialStore credentialStore,
        IDiagnosticSink diagnosticSink)
    {
        _providerSettingsStore = providerSettingsStore;
        _configurationStore = configurationStore;
        _credentialStore = credentialStore;
        _diagnosticSink = diagnosticSink;
    }

    public async Task<ProviderSettingsLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var providerReadStatus = GetReadStatus(providerRead);
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.ProviderSettingsRead,
            providerReadStatus,
            null).ConfigureAwait(false);
        var configurationRead = await _configurationStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var configurationReadStatus = GetReadStatus(configurationRead);
        var providerSettings = providerReadStatus == PersistenceStatus.Succeeded
            ? providerRead.Value!
            : new ProviderSettingsDocument(ProviderSettingsMigration.CurrentVersion, []);
        var configuration = configurationReadStatus == PersistenceStatus.Succeeded
            ? configurationRead.Value!
            : CreateDefaultConfiguration();
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.ConfigurationRead,
            configurationReadStatus,
            configuration.DefaultProvider.ProviderId).ConfigureAwait(false);
        var credentialRequired = UsesCredential(configuration.DefaultProvider);
        var credentialStatus = CanReadCredential(providerReadStatus, configurationReadStatus)
            ? credentialRequired
                ? await ReadCredentialStatusAsync(configuration.DefaultProvider, cancellationToken)
                    .ConfigureAwait(false)
                : PersistenceStatus.NotFound
            : UnavailableCredentialStatus(providerReadStatus, configurationReadStatus);

        return new ProviderSettingsLoadResult(
            providerSettings,
            configuration,
            providerReadStatus,
            configurationReadStatus,
            credentialStatus,
            credentialRequired);
    }

    public async Task<ProviderTranslationSettingsResult> LoadForTranslationAsync(
        CancellationToken cancellationToken)
    {
        var configurationRead = await _configurationStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var configurationReadStatus = GetReadStatus(configurationRead);
        if (configurationReadStatus != PersistenceStatus.Succeeded)
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ConfigurationRead,
                configurationReadStatus,
                null).ConfigureAwait(false);
            return ProviderTranslationSettingsResult.Failed(
                TranslateConfigurationStatus(configurationReadStatus),
                configurationReadStatus);
        }

        return await LoadForTranslationAsync(
            configurationRead.Value!.DefaultProvider,
            configurationRead.Value,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderTranslationSettingsResult> LoadForTranslationAsync(
        ProviderDescriptor selectedProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selectedProvider);
        var configurationRead = await _configurationStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var configurationReadStatus = GetReadStatus(configurationRead);
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.ConfigurationRead,
            configurationReadStatus,
            selectedProvider.ProviderId).ConfigureAwait(false);
        if (configurationReadStatus != PersistenceStatus.Succeeded)
        {
            return ProviderTranslationSettingsResult.Failed(
                TranslateConfigurationStatus(configurationReadStatus),
                configurationReadStatus);
        }

        return await LoadForTranslationAsync(
            selectedProvider,
            configurationRead.Value!,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderTranslationSettingsResult> LoadForTranslationAsync(
        ProviderDescriptor selectedProvider,
        Configuration configuration,
        CancellationToken cancellationToken)
    {
        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var providerReadStatus = GetReadStatus(providerRead);
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.ProviderSettingsRead,
            providerReadStatus,
            null).ConfigureAwait(false);
        if (providerReadStatus != PersistenceStatus.Succeeded)
        {
            return ProviderTranslationSettingsResult.Failed(
                TranslateProviderSettingsStatus(providerReadStatus),
                providerReadStatus);
        }

        var profile = providerRead.Value!.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.CanonicalProviderKey, CanonicalKey(selectedProvider), StringComparison.Ordinal));
        if (profile is null)
        {
            return ProviderTranslationSettingsResult.Failed(
                ProviderTranslationSettingsStatus.ProfileNotFound,
                PersistenceStatus.NotFound);
        }

        if (!UsesCredential(profile.Provider))
        {
            return ProviderTranslationSettingsResult.Success(profile, configuration, credential: null);
        }

        var credentialRead = await _credentialStore.GetAsync(
            new CredentialKey(profile.Provider.ProviderId, profile.Provider.InstanceId),
            cancellationToken).ConfigureAwait(false);
        var credential = credentialRead.Value;
        var credentialStatus = credentialRead.Status == PersistenceStatus.Succeeded && credential is null
            ? PersistenceStatus.InvalidData
            : credentialRead.Status;
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.CredentialRead,
            credentialStatus,
            profile.Provider.ProviderId).ConfigureAwait(false);
        if (credentialStatus is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound))
        {
            credential?.Dispose();
            return ProviderTranslationSettingsResult.Failed(
                ProviderTranslationSettingsStatus.CredentialUnavailable,
                credentialStatus);
        }

        if (credentialStatus == PersistenceStatus.NotFound)
        {
            credential?.Dispose();
            credential = null;
        }

        return ProviderTranslationSettingsResult.Success(
            profile,
            configuration,
            credential);
    }

    public async Task<PersistenceStatus> GetCredentialStatusAsync(
        ProviderDescriptor provider,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.CredentialRead,
                PersistenceStatus.InvalidData,
                null).ConfigureAwait(false);
            return PersistenceStatus.InvalidData;
        }

        try
        {
            provider.Validate();
        }
        catch (ContractValidationException)
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.CredentialRead,
                PersistenceStatus.InvalidData,
                null).ConfigureAwait(false);
            return PersistenceStatus.InvalidData;
        }

        return UsesCredential(provider)
            ? await ReadCredentialStatusAsync(provider, cancellationToken).ConfigureAwait(false)
            : PersistenceStatus.NotFound;
    }

    public async Task<ProviderSettingsSaveResult> SaveAsync(
        ProviderProfileSettings profile,
        HistoryRetention retention,
        string? password,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(profile, retention))
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ProviderSettingsWrite,
                PersistenceStatus.InvalidData,
                null).ConfigureAwait(false);
            return ProviderSettingsSaveResult.Invalid();
        }

        if (!UsesCredential(profile.Provider) && !string.IsNullOrEmpty(password))
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ProviderSettingsWrite,
                PersistenceStatus.InvalidData,
                profile.Provider.ProviderId).ConfigureAwait(false);
            return ProviderSettingsSaveResult.Invalid();
        }

        var existingRead = await _providerSettingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var existingReadStatus = GetReadStatus(existingRead);
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.ProviderSettingsRead,
            existingReadStatus,
            profile.Provider.ProviderId).ConfigureAwait(false);
        if (existingReadStatus is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound))
        {
            return ProviderSettingsSaveResult.ProviderSettingsReadFailure(existingReadStatus);
        }

        var existingProfiles = existingReadStatus == PersistenceStatus.Succeeded
            ? existingRead.Value!.Profiles
            : [];
        var document = new ProviderSettingsDocument(
            ProviderSettingsMigration.CurrentVersion,
            [.. existingProfiles.Where(candidate => !string.Equals(
                candidate.CanonicalProviderKey,
                profile.CanonicalProviderKey,
                StringComparison.Ordinal)), profile]);
        var providerWrite = await _providerSettingsStore.WriteAsync(document, cancellationToken)
            .ConfigureAwait(false);
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.ProviderSettingsWrite,
            providerWrite.Status,
            profile.Provider.ProviderId).ConfigureAwait(false);
        if (!providerWrite.Succeeded)
        {
            return ProviderSettingsSaveResult.ProviderSettingsWriteFailure(providerWrite.Status);
        }

        var configuration = new Configuration(
            SchemaVersion: 1,
            Version: ConfigurationMigration.CurrentVersion,
            DefaultProvider: profile.Provider,
            HistoryRetention: retention);
        var configurationWrite = await _configurationStore.WriteAsync(configuration, cancellationToken)
            .ConfigureAwait(false);
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.ConfigurationWrite,
            configurationWrite.Status,
            profile.Provider.ProviderId).ConfigureAwait(false);
        if (!configurationWrite.Succeeded)
        {
            return ProviderSettingsSaveResult.ConfigurationFailure(configurationWrite.Status);
        }

        PersistenceStatus? credentialStatus = null;
        if (UsesCredential(profile.Provider) && !string.IsNullOrEmpty(password))
        {
            using var secret = new CredentialSecret(password);
            var credentialWrite = await _credentialStore.SetAsync(
                new CredentialKey(profile.Provider.ProviderId, profile.Provider.InstanceId),
                secret,
                cancellationToken).ConfigureAwait(false);
            credentialStatus = credentialWrite.Status;
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.CredentialWrite,
                credentialWrite.Status,
                profile.Provider.ProviderId).ConfigureAwait(false);
            if (!credentialWrite.Succeeded)
            {
                return ProviderSettingsSaveResult.CredentialFailure(credentialWrite.Status);
            }
        }

        return ProviderSettingsSaveResult.Completed(credentialStatus);
    }

    public async Task<ProviderSettingsSaveResult> ClearCredentialAsync(
        ProviderDescriptor provider,
        CancellationToken cancellationToken)
    {
        if (provider is null)
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.CredentialRemove,
                PersistenceStatus.InvalidData,
                null).ConfigureAwait(false);
            return ProviderSettingsSaveResult.Invalid();
        }

        try
        {
            provider.Validate();
        }
        catch (ContractValidationException)
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.CredentialRemove,
                PersistenceStatus.InvalidData,
                null).ConfigureAwait(false);
            return ProviderSettingsSaveResult.Invalid();
        }

        if (!UsesCredential(provider))
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.CredentialRemove,
                PersistenceStatus.InvalidData,
                provider.ProviderId).ConfigureAwait(false);
            return ProviderSettingsSaveResult.Invalid();
        }

        var remove = await _credentialStore.RemoveAsync(
            new CredentialKey(provider.ProviderId, provider.InstanceId),
            cancellationToken).ConfigureAwait(false);
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.CredentialRemove,
            remove.Status,
            provider.ProviderId).ConfigureAwait(false);
        return remove.Status is PersistenceStatus.Succeeded or PersistenceStatus.NotFound
            ? ProviderSettingsSaveResult.CredentialCleared(remove.Status)
            : ProviderSettingsSaveResult.CredentialClearFailure(remove.Status);
    }

    private async Task<PersistenceStatus> ReadCredentialStatusAsync(
        ProviderDescriptor provider,
        CancellationToken cancellationToken)
    {
        var read = await _credentialStore.GetAsync(
            new CredentialKey(provider.ProviderId, provider.InstanceId),
            cancellationToken).ConfigureAwait(false);
        var status = read.Status == PersistenceStatus.Succeeded && read.Value is null
            ? PersistenceStatus.InvalidData
            : read.Status;
        await WritePersistenceDiagnosticAsync(
            DiagnosticEventId.CredentialRead,
            status,
            provider.ProviderId).ConfigureAwait(false);
        if (read.Value is { } secret)
        {
            using (secret)
            {
                return status;
            }
        }

        return status;
    }

    private async Task WritePersistenceDiagnosticAsync(
        DiagnosticEventId eventId,
        PersistenceStatus status,
        string? providerId)
    {
        await WriteDiagnosticAsync(
            eventId,
            ToDiagnosticOutcome(status),
            providerId,
            ToDiagnosticError(status)).ConfigureAwait(false);
    }

    private async Task WriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticOutcome outcome,
        string? providerId,
        DiagnosticErrorCode? errorCode)
    {
        try
        {
            await _diagnosticSink.WriteAsync(
                new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    outcome == DiagnosticOutcome.Failed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                    eventId,
                    outcome,
                    null,
                    providerId,
                    errorCode),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Diagnostics must never change the primary settings operation result.
        }
    }

    private static Configuration CreateDefaultConfiguration() => new(
        1,
        ConfigurationMigration.CurrentVersion,
        new ProviderDescriptor(TranslationProviderIds.OpenAiCompatible),
        DefaultRetention);

    private static bool UsesCredential(ProviderDescriptor provider) =>
        !string.Equals(provider.ProviderId, TranslationProviderIds.Google, StringComparison.Ordinal);

    private static bool TryValidate(ProviderProfileSettings? profile, HistoryRetention? retention)
    {
        if (profile is null || retention is null)
        {
            return false;
        }

        try
        {
            profile.Validate();
            retention.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private static string CanonicalKey(ProviderDescriptor provider) => provider.InstanceId is null
        ? provider.ProviderId
        : provider.ProviderId + ":" + provider.InstanceId;

    private static ProviderTranslationSettingsStatus TranslateProviderSettingsStatus(PersistenceStatus status) =>
        status == PersistenceStatus.NotFound
            ? ProviderTranslationSettingsStatus.ProviderSettingsNotFound
            : ProviderTranslationSettingsStatus.ProviderSettingsUnavailable;

    private static ProviderTranslationSettingsStatus TranslateConfigurationStatus(PersistenceStatus status) =>
        status == PersistenceStatus.NotFound
            ? ProviderTranslationSettingsStatus.ConfigurationNotFound
            : ProviderTranslationSettingsStatus.ConfigurationUnavailable;

    private static bool CanReadCredential(
        PersistenceStatus providerSettingsStatus,
        PersistenceStatus configurationStatus) =>
        (providerSettingsStatus is PersistenceStatus.Succeeded or PersistenceStatus.NotFound) &&
        (configurationStatus is PersistenceStatus.Succeeded or PersistenceStatus.NotFound);

    private static PersistenceStatus GetReadStatus<TValue>(PersistenceReadResult<TValue> result)
        where TValue : class =>
        result.Status == PersistenceStatus.Succeeded && result.Value is null
            ? PersistenceStatus.InvalidData
            : result.Status;

    private static PersistenceStatus UnavailableCredentialStatus(
        PersistenceStatus providerSettingsStatus,
        PersistenceStatus configurationStatus)
    {
        if (providerSettingsStatus == PersistenceStatus.Cancelled ||
            configurationStatus == PersistenceStatus.Cancelled)
        {
            return PersistenceStatus.Cancelled;
        }

        return providerSettingsStatus is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound)
            ? providerSettingsStatus
            : configurationStatus;
    }

    private static DiagnosticOutcome ToDiagnosticOutcome(PersistenceStatus status) => status switch
    {
        PersistenceStatus.Succeeded => DiagnosticOutcome.Succeeded,
        PersistenceStatus.NotFound => DiagnosticOutcome.NotFound,
        PersistenceStatus.Cancelled => DiagnosticOutcome.Cancelled,
        _ => DiagnosticOutcome.Failed,
    };

    private static DiagnosticErrorCode? ToDiagnosticError(PersistenceStatus status) => status switch
    {
        PersistenceStatus.InvalidData => DiagnosticErrorCode.InvalidData,
        PersistenceStatus.UnsupportedVersion => DiagnosticErrorCode.UnsupportedVersion,
        PersistenceStatus.CorruptData => DiagnosticErrorCode.CorruptData,
        PersistenceStatus.IoFailure => DiagnosticErrorCode.IoFailure,
        _ => null,
    };
}

internal sealed record ProviderSettingsLoadResult(
    ProviderSettingsDocument ProviderSettings,
    Configuration Configuration,
    PersistenceStatus ProviderSettingsStatus,
    PersistenceStatus ConfigurationStatus,
    PersistenceStatus CredentialStatus,
    bool CredentialRequired)
{
    public string StatusMessage => ProviderSettingsStatus is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound)
        ? AppStrings.Get("provider.status.settings_unavailable")
        : ConfigurationStatus is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound)
            ? AppStrings.Get("provider.status.configuration_unavailable")
            : !CredentialRequired
                ? AppStrings.Get("provider.status.credential_not_required")
                : CredentialStatus == PersistenceStatus.Succeeded
                ? AppStrings.Get("provider.status.credential_saved")
                : CredentialStatus == PersistenceStatus.NotFound
                    ? AppStrings.Get("provider.status.credential_not_found")
                    : AppStrings.Get("provider.status.credential_unavailable");
}

internal enum ProviderTranslationSettingsStatus
{
    Succeeded,
    ProviderSettingsNotFound,
    ProviderSettingsUnavailable,
    ConfigurationNotFound,
    ConfigurationUnavailable,
    ProfileNotFound,
    CredentialUnavailable,
}

internal sealed record ProviderTranslationSettingsResult(
    ProviderTranslationSettingsStatus Status,
    ProviderProfileSettings? Profile = null,
    Configuration? Configuration = null,
    CredentialSecret? Credential = null,
    PersistenceStatus? StorageStatus = null) : IDisposable
{
    public bool Succeeded => Status == ProviderTranslationSettingsStatus.Succeeded &&
        Profile is not null && Configuration is not null;

    public static ProviderTranslationSettingsResult Success(
        ProviderProfileSettings profile,
        Configuration configuration,
        CredentialSecret? credential) =>
        new(ProviderTranslationSettingsStatus.Succeeded, profile, configuration, credential);

    public static ProviderTranslationSettingsResult Failed(
        ProviderTranslationSettingsStatus status,
        PersistenceStatus? storageStatus) => new(status, StorageStatus: storageStatus);

    public void Dispose() => Credential?.Dispose();
}

internal sealed record ProviderSettingsSaveResult(
    PersistenceStatus ProviderSettingsStatus,
    PersistenceStatus ConfigurationStatus,
    PersistenceStatus? CredentialStatus,
    ProviderSettingsSaveStage Stage)
{
    public bool Succeeded => Stage == ProviderSettingsSaveStage.Completed;

    public bool RequiresSettingsReload => Stage is ProviderSettingsSaveStage.Completed or
        ProviderSettingsSaveStage.ConfigurationFailed or ProviderSettingsSaveStage.CredentialFailed;

    public string StatusMessage => Stage switch
    {
        ProviderSettingsSaveStage.Completed when CredentialStatus == PersistenceStatus.Succeeded => AppStrings.Get("provider.save.completed_with_credential"),
        ProviderSettingsSaveStage.Completed => AppStrings.Get("provider.save.completed"),
        ProviderSettingsSaveStage.Invalid => AppStrings.Get("provider.save.invalid"),
        ProviderSettingsSaveStage.ProviderSettingsReadFailed when ProviderSettingsStatus == PersistenceStatus.Cancelled =>
            AppStrings.Get("provider.save.read_cancelled"),
        ProviderSettingsSaveStage.ProviderSettingsReadFailed => AppStrings.Get("provider.save.read_failed"),
        ProviderSettingsSaveStage.ProviderSettingsWriteFailed when ProviderSettingsStatus == PersistenceStatus.Cancelled =>
            AppStrings.Get("provider.save.write_cancelled"),
        ProviderSettingsSaveStage.ProviderSettingsWriteFailed => AppStrings.Get("provider.save.write_failed"),
        ProviderSettingsSaveStage.ConfigurationFailed when ConfigurationStatus == PersistenceStatus.Cancelled =>
            AppStrings.Get("provider.save.configuration_cancelled"),
        ProviderSettingsSaveStage.ConfigurationFailed => AppStrings.Get("provider.save.configuration_failed"),
        ProviderSettingsSaveStage.CredentialFailed when CredentialStatus == PersistenceStatus.Cancelled =>
            AppStrings.Get("provider.save.credential_cancelled"),
        ProviderSettingsSaveStage.CredentialFailed => AppStrings.Get("provider.save.credential_failed"),
        ProviderSettingsSaveStage.CredentialCleared when CredentialStatus == PersistenceStatus.NotFound => AppStrings.Get("provider.save.credential_already_cleared"),
        ProviderSettingsSaveStage.CredentialCleared => AppStrings.Get("provider.save.credential_cleared"),
        ProviderSettingsSaveStage.CredentialClearFailed when CredentialStatus == PersistenceStatus.Cancelled =>
            AppStrings.Get("provider.save.clear_cancelled"),
        ProviderSettingsSaveStage.CredentialClearFailed => AppStrings.Get("provider.save.clear_failed"),
        _ => AppStrings.Get("provider.save.incomplete"),
    };

    public static ProviderSettingsSaveResult Completed(PersistenceStatus? credentialStatus) =>
        new(PersistenceStatus.Succeeded, PersistenceStatus.Succeeded, credentialStatus,
            ProviderSettingsSaveStage.Completed);

    public static ProviderSettingsSaveResult Invalid() =>
        new(PersistenceStatus.InvalidData, PersistenceStatus.InvalidData, null, ProviderSettingsSaveStage.Invalid);

    public static ProviderSettingsSaveResult ProviderSettingsReadFailure(PersistenceStatus status) =>
        new(status, PersistenceStatus.NotFound, null, ProviderSettingsSaveStage.ProviderSettingsReadFailed);

    public static ProviderSettingsSaveResult ProviderSettingsWriteFailure(PersistenceStatus status) =>
        new(status, PersistenceStatus.NotFound, null, ProviderSettingsSaveStage.ProviderSettingsWriteFailed);

    public static ProviderSettingsSaveResult ConfigurationFailure(PersistenceStatus status) =>
        new(PersistenceStatus.Succeeded, status, null, ProviderSettingsSaveStage.ConfigurationFailed);

    public static ProviderSettingsSaveResult CredentialFailure(PersistenceStatus status) =>
        new(PersistenceStatus.Succeeded, PersistenceStatus.Succeeded, status,
            ProviderSettingsSaveStage.CredentialFailed);

    public static ProviderSettingsSaveResult CredentialCleared(PersistenceStatus status) =>
        new(PersistenceStatus.Succeeded, PersistenceStatus.Succeeded, status,
            ProviderSettingsSaveStage.CredentialCleared);

    public static ProviderSettingsSaveResult CredentialClearFailure(PersistenceStatus status) =>
        new(PersistenceStatus.Succeeded, PersistenceStatus.Succeeded, status,
            ProviderSettingsSaveStage.CredentialClearFailed);
}

internal enum ProviderSettingsSaveStage
{
    Completed,
    Invalid,
    ProviderSettingsReadFailed,
    ProviderSettingsWriteFailed,
    ConfigurationFailed,
    CredentialFailed,
    CredentialCleared,
    CredentialClearFailed,
}
