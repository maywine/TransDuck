// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;

namespace TransDuck.Infrastructure.Persistence;

/// <summary>
/// Stores non-secret provider profiles as UTF-8 camelCase JSON using atomic replacement.
/// </summary>
public sealed class JsonProviderSettingsStore : IProviderSettingsStore, IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    /// <summary>Creates a store using the platform-resolved provider settings file.</summary>
    public JsonProviderSettingsStore(IApplicationDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).ProviderSettingsFilePath)
    {
    }

    /// <summary>Creates a store using an injected file path without writing it.</summary>
    public JsonProviderSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    /// <inheritdoc />
    public async Task<PersistenceReadResult<ProviderSettingsDocument>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(status);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(PersistenceStatus.NotFound);
            }

            var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
                .ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<ProviderSettingsDocument>(
                content,
                ContractJson.SerializerOptions) ?? throw new ContractValidationException(
                    ContractValidationError.MissingRequired,
                    "Provider settings JSON does not contain a document.");
            return ToReadResult(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (JsonException)
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (ContractValidationException)
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (DecoderFallbackException)
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (IOException)
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PersistenceResult> WriteAsync(
        ProviderSettingsDocument settings,
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
        catch (JsonException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
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
        Interlocked.Exchange(ref _disposeRequested, 1);
    }

    private static PersistenceReadResult<ProviderSettingsDocument> ToReadResult(
        ProviderSettingsDocument settings)
    {
        if (!TryValidateSettings(settings, out var status))
        {
            return PersistenceReadResult<ProviderSettingsDocument>.FromStatus(status);
        }

        return PersistenceReadResult<ProviderSettingsDocument>.Success(settings);
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

    private static bool TryValidateSettings(
        ProviderSettingsDocument? settings,
        out PersistenceStatus status)
    {
        if (settings is null)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }

        try
        {
            settings.Validate();
            status = settings.Version switch
            {
                > ProviderSettingsMigration.CurrentVersion => PersistenceStatus.UnsupportedVersion,
                < ProviderSettingsMigration.CurrentVersion => PersistenceStatus.InvalidData,
                _ => PersistenceStatus.Succeeded,
            };
            return status == PersistenceStatus.Succeeded;
        }
        catch (Exception exception) when (exception is ContractValidationException or ArgumentException)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }
    }
}
