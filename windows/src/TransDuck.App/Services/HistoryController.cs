// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.App.Services;

/// <summary>
/// Coordinates retained history reads and clears without exposing persistence details to the UI.
/// </summary>
internal sealed class HistoryController
{
    private static readonly HistoryRetention DefaultRetention = new(100, 30);
    private readonly IConfigurationStore _configurationStore;
    private readonly IQueryHistoryStore _historyStore;
    private readonly IDiagnosticSink _diagnosticSink;

    public HistoryController(
        IConfigurationStore configurationStore,
        IQueryHistoryStore historyStore,
        IDiagnosticSink diagnosticSink)
    {
        _configurationStore = configurationStore;
        _historyStore = historyStore;
        _diagnosticSink = diagnosticSink;
    }

    public async Task<HistoryLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var retention = DefaultRetention;
        var configurationStatus = PersistenceStatus.NotFound;
        var configurationWasRead = false;
        try
        {
            var configurationRead = await _configurationStore.ReadAsync(cancellationToken)
                .ConfigureAwait(false);
            configurationStatus = GetReadStatus(configurationRead);
            configurationWasRead = true;
            if (configurationStatus is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound))
            {
                await WriteDiagnosticAsync(
                    DiagnosticEventId.HistoryRead,
                    DiagnosticLevelFor(configurationStatus),
                    ToDiagnosticOutcome(configurationStatus),
                    ToDiagnosticError(configurationStatus)).ConfigureAwait(false);
                return new HistoryLoadResult(configurationStatus, null, [], 0, retention);
            }

            if (configurationStatus == PersistenceStatus.Succeeded)
            {
                retention = configurationRead.Value!.HistoryRetention;
            }

            var historyRead = await _historyStore.ReadAsync(retention, cancellationToken)
                .ConfigureAwait(false);
            if (historyRead.Status == PersistenceStatus.Succeeded && historyRead.CorruptLineCount > 0)
            {
                await WriteDiagnosticAsync(
                    DiagnosticEventId.HistoryRead,
                    DiagnosticLevel.Warning,
                    DiagnosticOutcome.Succeeded,
                    DiagnosticErrorCode.CorruptData).ConfigureAwait(false);
            }
            else
            {
                await WriteDiagnosticAsync(
                    DiagnosticEventId.HistoryRead,
                    DiagnosticLevelFor(historyRead.Status),
                    ToDiagnosticOutcome(historyRead.Status),
                    ToDiagnosticError(historyRead.Status)).ConfigureAwait(false);
            }

            return new HistoryLoadResult(
                configurationStatus,
                historyRead.Status,
                historyRead.Entries,
                historyRead.CorruptLineCount,
                retention);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.HistoryRead,
                DiagnosticLevel.Information,
                DiagnosticOutcome.Cancelled,
                null).ConfigureAwait(false);
            return configurationWasRead
                ? new HistoryLoadResult(configurationStatus, PersistenceStatus.Cancelled, [], 0, retention)
                : new HistoryLoadResult(PersistenceStatus.Cancelled, null, [], 0, retention);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.HistoryRead,
                DiagnosticLevel.Error,
                DiagnosticOutcome.Failed,
                DiagnosticErrorCode.IoFailure).ConfigureAwait(false);
            return configurationWasRead
                ? new HistoryLoadResult(configurationStatus, PersistenceStatus.IoFailure, [], 0, retention)
                : new HistoryLoadResult(PersistenceStatus.IoFailure, null, [], 0, retention);
        }
    }

    public async Task<HistoryClearResult> ClearAsync(CancellationToken cancellationToken)
    {
        try
        {
            var clear = await _historyStore.ClearAsync(cancellationToken).ConfigureAwait(false);
            await WriteDiagnosticAsync(
                DiagnosticEventId.HistoryClear,
                DiagnosticLevelFor(clear.Status),
                ToDiagnosticOutcome(clear.Status),
                ToDiagnosticError(clear.Status)).ConfigureAwait(false);
            return new HistoryClearResult(clear.Status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.HistoryClear,
                DiagnosticLevel.Information,
                DiagnosticOutcome.Cancelled,
                null).ConfigureAwait(false);
            return new HistoryClearResult(PersistenceStatus.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.HistoryClear,
                DiagnosticLevel.Error,
                DiagnosticOutcome.Failed,
                DiagnosticErrorCode.IoFailure).ConfigureAwait(false);
            return new HistoryClearResult(PersistenceStatus.IoFailure);
        }
    }

    private async Task WriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticLevel level,
        DiagnosticOutcome outcome,
        DiagnosticErrorCode? errorCode)
    {
        try
        {
            await _diagnosticSink.WriteAsync(
                new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    level,
                    eventId,
                    outcome,
                    ErrorCode: errorCode),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Diagnostics must not change user-visible history state.
        }
    }

    private static PersistenceStatus GetReadStatus<TValue>(PersistenceReadResult<TValue> result)
        where TValue : class =>
        result.Status == PersistenceStatus.Succeeded && result.Value is null
            ? PersistenceStatus.InvalidData
            : result.Status;

    private static DiagnosticLevel DiagnosticLevelFor(PersistenceStatus status) => status switch
    {
        PersistenceStatus.InvalidData or
        PersistenceStatus.UnsupportedVersion or
        PersistenceStatus.CorruptData or
        PersistenceStatus.IoFailure => DiagnosticLevel.Error,
        _ => DiagnosticLevel.Information,
    };

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

/// <summary>
/// Describes a retained history read without embedding any history content in status fields.
/// </summary>
internal sealed record HistoryLoadResult(
    PersistenceStatus ConfigurationStatus,
    PersistenceStatus? HistoryStatus,
    IReadOnlyList<HistoryEntry> Entries,
    int CorruptLineCount,
    HistoryRetention Retention)
{
    public bool Succeeded => HistoryStatus == PersistenceStatus.Succeeded;

    public bool UsedDefaultRetention => ConfigurationStatus == PersistenceStatus.NotFound;
}

/// <summary>
/// Describes a clear operation where a missing file is already an empty history.
/// </summary>
internal sealed record HistoryClearResult(PersistenceStatus Status)
{
    public bool Succeeded => Status is PersistenceStatus.Succeeded or PersistenceStatus.NotFound;

    public bool WasAlreadyEmpty => Status == PersistenceStatus.NotFound;
}
