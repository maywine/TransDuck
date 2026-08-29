// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.Platform.Windows.Persistence;

/// <summary>
/// Stores the supported v1 configuration as UTF-8 JSON using atomic replacement.
/// </summary>
public sealed class JsonConfigurationStore : IConfigurationStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    /// <summary>Creates a store using the configuration file resolved by Windows data paths.</summary>
    public JsonConfigurationStore(WindowsDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).ConfigurationFilePath)
    {
    }

    /// <summary>Creates a store using an injected file path without writing it.</summary>
    public JsonConfigurationStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public async Task<PersistenceReadResult<Configuration>> ReadAsync(CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<Configuration>.FromStatus(status);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.NotFound);
            }

            var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
                .ConfigureAwait(false);
            var configuration = ContractJson.Deserialize<Configuration>(content);
            return configuration.Version switch
            {
                > ConfigurationMigration.CurrentVersion =>
                    PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.UnsupportedVersion),
                < ConfigurationMigration.CurrentVersion =>
                    PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.InvalidData),
                _ => PersistenceReadResult<Configuration>.Success(configuration),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (ContractValidationException)
        {
            return PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (DecoderFallbackException)
        {
            return PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (IOException)
        {
            return PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceReadResult<Configuration>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PersistenceResult> WriteAsync(
        Configuration configuration,
        CancellationToken cancellationToken)
    {
        if (!TryValidateConfiguration(configuration, out var status))
        {
            return PersistenceResult.FromStatus(status);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } entryFailure)
        {
            return PersistenceResult.FromStatus(entryFailure);
        }

        try
        {
            var content = ContractJson.Serialize(configuration);
            await AtomicFileWriter.WriteUtf8Async(_filePath, content, cancellationToken)
                .ConfigureAwait(false);
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

    private static bool TryValidateConfiguration(Configuration? configuration, out PersistenceStatus status)
    {
        if (configuration is null)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }

        try
        {
            configuration.Validate();
            status = configuration.Version switch
            {
                > ConfigurationMigration.CurrentVersion => PersistenceStatus.UnsupportedVersion,
                < ConfigurationMigration.CurrentVersion => PersistenceStatus.InvalidData,
                _ => PersistenceStatus.Succeeded,
            };
            return status == PersistenceStatus.Succeeded;
        }
        catch (ContractValidationException)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }
    }
}
