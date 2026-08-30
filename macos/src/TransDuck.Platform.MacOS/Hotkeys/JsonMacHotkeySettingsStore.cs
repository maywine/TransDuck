using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Infrastructure.Persistence;
using TransDuck.Platform.MacOS.Persistence;

namespace TransDuck.Platform.MacOS.Hotkeys;

public sealed class JsonMacHotkeySettingsStore : IDisposable
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    public JsonMacHotkeySettingsStore(MacDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).HotkeySettingsFilePath)
    {
    }

    public JsonMacHotkeySettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public async Task<PersistenceReadResult<MacHotkeySettings>> ReadAsync(CancellationToken cancellationToken)
    {
        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<MacHotkeySettings>.FromStatus(status);
        }

        try
        {
            if (!File.Exists(_filePath))
            {
                return PersistenceReadResult<MacHotkeySettings>.FromStatus(PersistenceStatus.NotFound);
            }

            var content = await AtomicFileWriter.ReadUtf8Async(_filePath, cancellationToken)
                .ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<MacHotkeySettings>(
                content,
                ContractJson.SerializerOptions) ?? throw new ContractValidationException(
                    ContractValidationError.MissingRequired,
                    "macOS hotkey settings JSON does not contain a document.");
            settings.Validate();
            return PersistenceReadResult<MacHotkeySettings>.Success(settings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<MacHotkeySettings>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (Exception exception) when (
            exception is JsonException or ContractValidationException or DecoderFallbackException or ArgumentException)
        {
            return PersistenceReadResult<MacHotkeySettings>.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PersistenceReadResult<MacHotkeySettings>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PersistenceResult> WriteAsync(
        MacHotkeySettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(settings, out var validationStatus))
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
        catch (Exception exception) when (
            exception is JsonException or ContractValidationException or ArgumentException)
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

    private static bool TryValidate(MacHotkeySettings? settings, out PersistenceStatus status)
    {
        if (settings is null)
        {
            status = PersistenceStatus.InvalidData;
            return false;
        }

        if (settings.Version > MacHotkeySettingsMigration.CurrentVersion)
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
