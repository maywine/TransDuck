// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;

namespace TransDuck.Infrastructure.Persistence;

/// <summary>
/// Atomically stores the selected translation and dictionary sources as UTF-8 JSON.
/// </summary>
public sealed class JsonQuerySourceSettingsStore : IQuerySourceSettingsStore, IDisposable
{
    public const string FileName = "query-sources.v1.json";

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    public JsonQuerySourceSettingsStore(IApplicationDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).QuerySourceSettingsFilePath)
    {
    }

    public JsonQuerySourceSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<PersistenceReadResult<QuerySourceSettings>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<QuerySourceSettings>.FromStatus(status);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return PersistenceReadResult<QuerySourceSettings>.FromStatus(PersistenceStatus.NotFound);
            }

            var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
                .ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<QuerySourceSettings>(
                content,
                ContractJson.SerializerOptions) ?? throw new ContractValidationException(
                    ContractValidationError.MissingRequired,
                    "Query source settings JSON does not contain a document.");
            return ToReadResult(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<QuerySourceSettings>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (Exception exception) when (exception is JsonException or ContractValidationException or DecoderFallbackException)
        {
            return PersistenceReadResult<QuerySourceSettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PersistenceReadResult<QuerySourceSettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PersistenceResult> WriteAsync(
        QuerySourceSettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryValidateSettings(settings, out var validationStatus))
        {
            return PersistenceResult.FromStatus(validationStatus);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceResult.FromStatus(status);
        }

        try
        {
            var content = JsonSerializer.Serialize(settings, ContractJson.SerializerOptions);
            await AtomicFileWriter.WriteUtf8Async(_filePath, content, cancellationToken)
                .ConfigureAwait(false);
            return PersistenceResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (Exception exception) when (exception is JsonException or ContractValidationException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => Interlocked.Exchange(ref _disposeRequested, 1);

    private static PersistenceReadResult<QuerySourceSettings> ToReadResult(QuerySourceSettings settings) =>
        TryValidateSettings(settings, out var status)
            ? PersistenceReadResult<QuerySourceSettings>.Success(settings)
            : PersistenceReadResult<QuerySourceSettings>.FromStatus(status);

    private async Task<PersistenceStatus?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return PersistenceStatus.IoFailure;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposeRequested) == 0)
            {
                return null;
            }

            _gate.Release();
            return PersistenceStatus.IoFailure;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceStatus.Cancelled;
        }
    }

    private static bool TryValidateSettings(QuerySourceSettings? settings, out PersistenceStatus status)
    {
        if (settings is null)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }

        if (settings.Version > QuerySourceSettingsMigration.CurrentVersion)
        {
            status = PersistenceStatus.UnsupportedVersion;
            return false;
        }

        try
        {
            settings.Validate();
            status = PersistenceStatus.Succeeded;
            return true;
        }
        catch (ContractValidationException)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }
    }
}
