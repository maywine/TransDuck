// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;

namespace TransDuck.App.Services;

/// <summary>
/// Keeps query-source persistence out of UI code-behind and provides single-provider migration defaults.
/// </summary>
internal sealed class QuerySourceSettingsController
{
    private readonly IQuerySourceSettingsStore _store;

    public QuerySourceSettingsController(IQuerySourceSettingsStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<QuerySourceSettingsLoadResult> LoadAsync(
        ProviderDescriptor defaultProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(defaultProvider);
        var read = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        return new QuerySourceSettingsLoadResult(
            read.Succeeded ? read.Value! : QuerySourceSettings.CreateDefault(defaultProvider),
            read.Status);
    }

    public async Task<PersistenceResult> SaveAsync(
        QuerySourceSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return await _store.WriteAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed record QuerySourceSettingsLoadResult(
    QuerySourceSettings Settings,
    PersistenceStatus Status)
{
    public bool UsesMigrationDefault => Status == PersistenceStatus.NotFound;

    public bool Succeeded => Status is PersistenceStatus.Succeeded or PersistenceStatus.NotFound;
}
