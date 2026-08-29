// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.Platform.Windows.Persistence;

/// <summary>
/// Persists v1 history as UTF-8 JSON Lines while compacting retained valid entries atomically.
/// </summary>
public sealed class JsonLinesHistoryStore : IQueryHistoryStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private int _disposeRequested;

    /// <summary>Creates a store using the history file resolved by Windows data paths.</summary>
    public JsonLinesHistoryStore(WindowsDataPaths paths, TimeProvider? timeProvider = null)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).HistoryFilePath, timeProvider)
    {
    }

    /// <summary>Creates a store using an injected JSON Lines file path without writing it.</summary>
    public JsonLinesHistoryStore(string filePath, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<HistoryReadResult> ReadAsync(
        HistoryRetention retention,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRetention(retention))
        {
            return new HistoryReadResult(PersistenceStatus.InvalidData, []);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return new HistoryReadResult(status, []);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return new HistoryReadResult(PersistenceStatus.NotFound, []);
            }

            var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return new HistoryReadResult(
                PersistenceStatus.Succeeded,
                ApplyRetention(loaded.Entries, retention),
                loaded.CorruptLineCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HistoryReadResult(PersistenceStatus.Cancelled, []);
        }
        catch (FileNotFoundException)
        {
            return new HistoryReadResult(PersistenceStatus.NotFound, []);
        }
        catch (DecoderFallbackException)
        {
            return new HistoryReadResult(PersistenceStatus.CorruptData, []);
        }
        catch (IOException)
        {
            return new HistoryReadResult(PersistenceStatus.IoFailure, []);
        }
        catch (UnauthorizedAccessException)
        {
            return new HistoryReadResult(PersistenceStatus.IoFailure, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<HistoryWriteResult> AppendAsync(
        HistoryEntry entry,
        HistoryRetention retention,
        CancellationToken cancellationToken)
    {
        if (!TryValidateEntry(entry) || !TryValidateRetention(retention))
        {
            return new HistoryWriteResult(PersistenceStatus.InvalidData);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return new HistoryWriteResult(status);
        }

        try
        {
            var loaded = File.Exists(_filePath)
                ? await LoadAsync(cancellationToken).ConfigureAwait(false)
                : HistoryLoad.Empty;
            var retained = ApplyRetention([.. loaded.Entries, entry], retention);
            var content = string.Join('\n', retained.Select(ContractJson.Serialize));
            if (content.Length > 0)
            {
                content += "\n";
            }

            await AtomicFileWriter.WriteUtf8Async(_filePath, content, cancellationToken)
                .ConfigureAwait(false);
            return new HistoryWriteResult(PersistenceStatus.Succeeded, loaded.CorruptLineCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new HistoryWriteResult(PersistenceStatus.Cancelled);
        }
        catch (ContractValidationException)
        {
            return new HistoryWriteResult(PersistenceStatus.InvalidData);
        }
        catch (DecoderFallbackException)
        {
            return new HistoryWriteResult(PersistenceStatus.CorruptData);
        }
        catch (IOException)
        {
            return new HistoryWriteResult(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return new HistoryWriteResult(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PersistenceResult> ClearAsync(CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceResult.FromStatus(status);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(_filePath))
            {
                return PersistenceResult.FromStatus(PersistenceStatus.NotFound);
            }

            await AtomicFileWriter.WriteUtf8Async(_filePath, string.Empty, cancellationToken)
                .ConfigureAwait(false);
            return PersistenceResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (IOException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }
    }

    private async Task<HistoryLoad> LoadAsync(CancellationToken cancellationToken)
    {
        var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
            .ConfigureAwait(false);
        var entries = new List<HistoryEntry>();
        var corruptLineCount = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                entries.Add(ContractJson.Deserialize<HistoryEntry>(line));
            }
            catch (ContractValidationException)
            {
                corruptLineCount++;
            }
        }

        return new HistoryLoad(Order(entries), corruptLineCount);
    }

    private IReadOnlyList<HistoryEntry> ApplyRetention(
        IEnumerable<HistoryEntry> entries,
        HistoryRetention retention)
    {
        var retained = Order(entries);
        if (retention.MaxAgeDays > 0)
        {
            var cutoff = _timeProvider.GetUtcNow().AddDays(-retention.MaxAgeDays);
            retained = retained.Where(entry => entry.CreatedAt >= cutoff).ToArray();
        }

        return retention.MaxEntries > 0
            ? retained.Take(retention.MaxEntries).ToArray()
            : retained;
    }

    private static IReadOnlyList<HistoryEntry> Order(IEnumerable<HistoryEntry> entries) =>
        entries.OrderByDescending(entry => entry.CreatedAt)
            .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
            .ToArray();

    private async Task<PersistenceStatus?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return PersistenceStatus.IoFailure;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                _gate.Release();
                return PersistenceStatus.IoFailure;
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceStatus.Cancelled;
        }
    }

    private static bool TryValidateEntry(HistoryEntry? entry)
    {
        if (entry is null)
        {
            return false;
        }

        try
        {
            entry.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private static bool TryValidateRetention(HistoryRetention? retention)
    {
        if (retention is null)
        {
            return false;
        }

        try
        {
            retention.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private sealed record HistoryLoad(IReadOnlyList<HistoryEntry> Entries, int CorruptLineCount)
    {
        public static HistoryLoad Empty { get; } = new([], 0);
    }
}
