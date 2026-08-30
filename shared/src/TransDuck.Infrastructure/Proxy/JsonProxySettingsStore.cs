// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Infrastructure.Persistence;

namespace TransDuck.Infrastructure.Proxy;

/// <summary>
/// Atomically stores the supported platform-neutral proxy policy as UTF-8 JSON.
/// </summary>
public sealed class JsonProxySettingsStore : IDisposable
{
    /// <summary>Gets the file name used below the platform application data root.</summary>
    public const string FileName = "proxy-settings.v1.json";

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    /// <summary>Creates a store using the platform-resolved proxy settings file.</summary>
    public JsonProxySettingsStore(IApplicationDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).ProxySettingsFilePath)
    {
    }

    /// <summary>Creates a store using an injected file path without writing it.</summary>
    public JsonProxySettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    /// <summary>Reads supported proxy settings or returns NotFound when no document exists.</summary>
    public async Task<PersistenceReadResult<ProxySettings>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(status);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return PersistenceReadResult<ProxySettings>.FromStatus(
                    PersistenceStatus.NotFound);
            }

            var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
                .ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<ProxySettings>(
                content,
                ContractJson.SerializerOptions) ?? throw new ContractValidationException(
                    ContractValidationError.MissingRequired,
                    "Proxy settings JSON does not contain a document.");
            return ToReadResult(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (JsonException)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (ContractValidationException)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (DecoderFallbackException)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (ArgumentException)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (IOException)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes supported proxy settings atomically.</summary>
    public async Task<PersistenceResult> WriteAsync(
        ProxySettings settings,
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
        catch (ArgumentException)
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

    private static PersistenceReadResult<ProxySettings> ToReadResult(
        ProxySettings settings)
    {
        if (!TryValidateSettings(settings, out var status))
        {
            return PersistenceReadResult<ProxySettings>.FromStatus(status);
        }

        return PersistenceReadResult<ProxySettings>.Success(settings);
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
        ProxySettings? settings,
        out PersistenceStatus status)
    {
        if (settings is null)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }

        if (settings.Version > ProxySettingsMigration.CurrentVersion)
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
