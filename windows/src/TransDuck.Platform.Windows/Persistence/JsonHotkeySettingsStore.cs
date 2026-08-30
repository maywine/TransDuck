// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Infrastructure.Persistence;
using TransDuck.Platform.Windows.Hotkeys;

namespace TransDuck.Platform.Windows.Persistence;

/// <summary>
/// Stores the supported hotkey settings as UTF-8 JSON using atomic replacement.
/// </summary>
public sealed class JsonHotkeySettingsStore : IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    /// <summary>Creates a store using the hotkey settings file resolved by Windows data paths.</summary>
    public JsonHotkeySettingsStore(WindowsDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).HotkeySettingsFilePath)
    {
    }

    /// <summary>Creates a store using an injected file path without writing it.</summary>
    public JsonHotkeySettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    /// <summary>Reads supported hotkey settings or returns NotFound when no document exists.</summary>
    public async Task<PersistenceReadResult<HotkeySettings>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(status);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return PersistenceReadResult<HotkeySettings>.FromStatus(PersistenceStatus.NotFound);
            }

            var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
                .ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<HotkeySettings>(
                content,
                ContractJson.SerializerOptions) ?? throw new ContractValidationException(
                    ContractValidationError.MissingRequired,
                    "Hotkey settings JSON does not contain a document.");
            return ToReadResult(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (JsonException)
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (ContractValidationException)
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (DecoderFallbackException)
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (IOException)
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes supported hotkey settings atomically.</summary>
    public async Task<PersistenceResult> WriteAsync(
        HotkeySettings settings,
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

    private static PersistenceReadResult<HotkeySettings> ToReadResult(HotkeySettings settings)
    {
        if (!TryValidateSettings(settings, out var status))
        {
            return PersistenceReadResult<HotkeySettings>.FromStatus(status);
        }

        return PersistenceReadResult<HotkeySettings>.Success(settings);
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

    private static bool TryValidateSettings(HotkeySettings? settings, out PersistenceStatus status)
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
                > HotkeySettingsMigration.CurrentVersion => PersistenceStatus.UnsupportedVersion,
                < HotkeySettingsMigration.CurrentVersion => PersistenceStatus.InvalidData,
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
