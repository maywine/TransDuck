// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.Platform.Windows.Proxy;

/// <summary>
/// Atomically stores the supported Windows-only proxy policy as UTF-8 JSON.
/// </summary>
public sealed class JsonWindowsProxySettingsStore : IDisposable
{
    /// <summary>Gets the file name used below the Windows application data root.</summary>
    public const string FileName = "proxy-settings.v1.json";

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    /// <summary>Creates a store using the proxy settings file below the Windows data root.</summary>
    public JsonWindowsProxySettingsStore(WindowsDataPaths paths)
        : this(Path.Combine(
            (paths ?? throw new ArgumentNullException(nameof(paths))).RootDirectory,
            FileName))
    {
    }

    /// <summary>Creates a store using an injected file path without writing it.</summary>
    public JsonWindowsProxySettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    /// <summary>Reads supported proxy settings or returns NotFound when no document exists.</summary>
    public async Task<PersistenceReadResult<WindowsProxySettings>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(status);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return PersistenceReadResult<WindowsProxySettings>.FromStatus(
                    PersistenceStatus.NotFound);
            }

            var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
                .ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<WindowsProxySettings>(
                content,
                ContractJson.SerializerOptions) ?? throw new ContractValidationException(
                    ContractValidationError.MissingRequired,
                    "Proxy settings JSON does not contain a document.");
            return ToReadResult(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (JsonException)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (ContractValidationException)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (DecoderFallbackException)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (ArgumentException)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (IOException)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes supported proxy settings atomically.</summary>
    public async Task<PersistenceResult> WriteAsync(
        WindowsProxySettings settings,
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

    private static PersistenceReadResult<WindowsProxySettings> ToReadResult(
        WindowsProxySettings settings)
    {
        if (!TryValidateSettings(settings, out var status))
        {
            return PersistenceReadResult<WindowsProxySettings>.FromStatus(status);
        }

        return PersistenceReadResult<WindowsProxySettings>.Success(settings);
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
        WindowsProxySettings? settings,
        out PersistenceStatus status)
    {
        if (settings is null)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }

        if (settings.Version > WindowsProxySettingsMigration.CurrentVersion)
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
