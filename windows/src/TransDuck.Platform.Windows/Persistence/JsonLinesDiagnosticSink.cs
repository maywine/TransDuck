// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.Platform.Windows.Persistence;

/// <summary>
/// Appends only validated structured diagnostics as UTF-8 JSON Lines.
/// </summary>
public sealed class JsonLinesDiagnosticSink : IDiagnosticSink, IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    /// <summary>Creates a sink using the diagnostic file resolved by Windows data paths.</summary>
    public JsonLinesDiagnosticSink(WindowsDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).DiagnosticFilePath)
    {
    }

    /// <summary>Creates a sink using an injected JSON Lines file path without writing it.</summary>
    public JsonLinesDiagnosticSink(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public async Task<PersistenceResult> WriteAsync(
        DiagnosticEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        if (!TryValidateDiagnostic(diagnosticEvent))
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceResult.FromStatus(status);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
            }

            Directory.CreateDirectory(directory);
            var line = JsonSerializer.Serialize(diagnosticEvent, ContractJson.SerializerOptions);
            await using var stream = new FileStream(
                _filePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            await using var writer = new StreamWriter(stream, Utf8NoBom, leaveOpen: false);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return PersistenceResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (ContractValidationException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
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

    private static bool TryValidateDiagnostic(DiagnosticEvent? diagnosticEvent)
    {
        if (diagnosticEvent is null)
        {
            return false;
        }

        try
        {
            diagnosticEvent.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }
}
