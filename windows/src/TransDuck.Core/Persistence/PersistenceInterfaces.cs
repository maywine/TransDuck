// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;

namespace TransDuck.Core.Persistence;

/// <summary>
/// Defines the current supported configuration document migration version.
/// </summary>
public static class ConfigurationMigration
{
    /// <summary>Gets the only configuration Version accepted by this implementation.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Defines the current supported provider profile document version.
/// </summary>
public static class ProviderSettingsMigration
{
    /// <summary>Gets the only provider profile document Version accepted by this implementation.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Reads and atomically writes non-secret provider profile settings.
/// </summary>
public interface IProviderSettingsStore
{
    /// <summary>Reads provider settings or returns NotFound when no document exists.</summary>
    Task<PersistenceReadResult<ProviderSettingsDocument>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Writes a supported provider settings document atomically.</summary>
    Task<PersistenceResult> WriteAsync(
        ProviderSettingsDocument settings,
        CancellationToken cancellationToken);
}

/// <summary>
/// Reads and atomically writes the versioned non-secret configuration document.
/// </summary>
public interface IConfigurationStore
{
    /// <summary>Reads the current configuration or returns NotFound when no document exists.</summary>
    Task<PersistenceReadResult<Configuration>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Writes a supported configuration atomically.</summary>
    Task<PersistenceResult> WriteAsync(Configuration configuration, CancellationToken cancellationToken);
}

/// <summary>
/// Reads and writes a credential without exposing storage or encryption implementation details.
/// </summary>
public interface ICredentialStore
{
    /// <summary>
    /// Reads a credential or returns NotFound when no credential exists; callers must dispose a returned secret.
    /// </summary>
    Task<PersistenceReadResult<CredentialSecret>> GetAsync(
        CredentialKey key,
        CancellationToken cancellationToken);

    /// <summary>Persists a credential for a validated key.</summary>
    Task<PersistenceResult> SetAsync(
        CredentialKey key,
        CredentialSecret secret,
        CancellationToken cancellationToken);

    /// <summary>Removes a credential or returns NotFound when no credential exists.</summary>
    Task<PersistenceResult> RemoveAsync(CredentialKey key, CancellationToken cancellationToken);
}

/// <summary>
/// Returns history data while reporting corrupt lines without exposing their content.
/// </summary>
public sealed record HistoryReadResult(
    PersistenceStatus Status,
    IReadOnlyList<HistoryEntry> Entries,
    int CorruptLineCount = 0)
{
    /// <summary>Gets whether valid history records were read successfully.</summary>
    public bool Succeeded => Status == PersistenceStatus.Succeeded;
}

/// <summary>
/// Reports an append/rewrite result and the number of corrupt lines discarded during compaction.
/// </summary>
public sealed record HistoryWriteResult(PersistenceStatus Status, int CorruptLineCount = 0)
{
    /// <summary>Gets whether the append and retention rewrite completed successfully.</summary>
    public bool Succeeded => Status == PersistenceStatus.Succeeded;
}

/// <summary>
/// Persists user-owned history without sending its content to diagnostics.
/// </summary>
public interface IQueryHistoryStore
{
    /// <summary>
    /// Reads valid entries in descending CreatedAt order after applying retention; zero retention limits are unbounded.
    /// </summary>
    Task<HistoryReadResult> ReadAsync(
        HistoryRetention retention,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends an entry and atomically rewrites retained valid history; zero retention limits are unbounded.
    /// </summary>
    Task<HistoryWriteResult> AppendAsync(
        HistoryEntry entry,
        HistoryRetention retention,
        CancellationToken cancellationToken);

    /// <summary>Clears persisted history or returns NotFound when no history file exists.</summary>
    Task<PersistenceResult> ClearAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Appends only a closed set of safe structured diagnostic fields.
/// </summary>
public interface IDiagnosticSink
{
    /// <summary>Writes one safe diagnostic event.</summary>
    Task<PersistenceResult> WriteAsync(DiagnosticEvent diagnosticEvent, CancellationToken cancellationToken);
}
