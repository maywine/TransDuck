// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Proxy;

namespace TransDuck.App.Services;

/// <summary>
/// Coordinates persisted Windows proxy policy and generation-safe runtime transport updates.
/// </summary>
internal sealed class ProxySettingsController : IDisposable
{
    private readonly JsonWindowsProxySettingsStore _settingsStore;
    private readonly ProxyHttpClientPool _clientPool;
    private readonly IDiagnosticSink _diagnosticSink;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WindowsProxySettings _currentSettings;
    private ProxySettingsInitializationResult _initializationResult =
        ProxySettingsInitializationResult.Loading();
    private string _statusMessage = AppStrings.Get("proxy.status.loading");
    private bool _isInitialized;
    private bool _disposed;

    public ProxySettingsController(
        JsonWindowsProxySettingsStore settingsStore,
        ProxyHttpClientPool clientPool,
        IDiagnosticSink diagnosticSink)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _clientPool = clientPool ?? throw new ArgumentNullException(nameof(clientPool));
        _diagnosticSink = diagnosticSink ?? throw new ArgumentNullException(nameof(diagnosticSink));
        _currentSettings = _clientPool.CurrentSettings;
    }

    public event EventHandler? StateChanged;

    public WindowsProxySettings CurrentSettings => _currentSettings;

    public bool IsInitialized => _isInitialized;

    public string StatusMessage => _statusMessage;

    public async Task<ProxySettingsInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var cancelled = ProxySettingsInitializationResult.ReadFailure(
                PersistenceStatus.Cancelled,
                _currentSettings);
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ProxySettingsRead,
                PersistenceStatus.Cancelled).ConfigureAwait(false);
            SetState(cancelled, initialized: false);
            return cancelled;
        }

        try
        {
            if (_isInitialized)
            {
                return _initializationResult;
            }

            var readStatus = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
            ProxySettingsInitializationResult result;
            if (readStatus.Status == PersistenceStatus.Succeeded)
            {
                result = TryApplyReadSettings(readStatus.Settings!);
            }
            else if (readStatus.Status == PersistenceStatus.NotFound)
            {
                var defaultResult = TryApplyReadSettings(WindowsProxySettings.Default);
                result = defaultResult.Stage == ProxySettingsInitializationStage.ApplyFailed
                    ? defaultResult
                    : ProxySettingsInitializationResult.NotFound(_currentSettings);
            }
            else
            {
                result = ProxySettingsInitializationResult.ReadFailure(readStatus.Status, _currentSettings);
            }

            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ProxySettingsRead,
                result.DiagnosticStatus).ConfigureAwait(false);
            SetState(result, initialized: result.Stage != ProxySettingsInitializationStage.Cancelled);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ProxySettingsSaveResult> SaveAsync(
        WindowsProxySettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(settings))
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ProxySettingsWrite,
                PersistenceStatus.InvalidData).ConfigureAwait(false);
            var invalid = ProxySettingsSaveResult.Invalid();
            SetState(invalid);
            return invalid;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ProxySettingsWrite,
                PersistenceStatus.Cancelled).ConfigureAwait(false);
            var cancelled = ProxySettingsSaveResult.WriteFailure(PersistenceStatus.Cancelled);
            SetState(cancelled);
            return cancelled;
        }

        try
        {
            if (_disposed)
            {
                var unavailable = ProxySettingsSaveResult.ApplyFailed();
                SetState(unavailable);
                return unavailable;
            }

            var writeStatus = await WriteSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
            if (writeStatus != PersistenceStatus.Succeeded)
            {
                await WritePersistenceDiagnosticAsync(
                    DiagnosticEventId.ProxySettingsWrite,
                    writeStatus).ConfigureAwait(false);
                var writeFailure = ProxySettingsSaveResult.WriteFailure(writeStatus);
                SetState(writeFailure);
                return writeFailure;
            }

            try
            {
                _ = _clientPool.Update(settings);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                await WritePersistenceDiagnosticAsync(
                    DiagnosticEventId.ProxySettingsWrite,
                    PersistenceStatus.IoFailure).ConfigureAwait(false);
                var applyFailure = ProxySettingsSaveResult.ApplyFailed();
                SetState(applyFailure);
                return applyFailure;
            }

            _currentSettings = _clientPool.CurrentSettings;
            var completed = ProxySettingsSaveResult.Completed(_currentSettings.Mode);
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.ProxySettingsWrite,
                PersistenceStatus.Succeeded).ConfigureAwait(false);
            SetState(completed);
            return completed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private ProxySettingsInitializationResult TryApplyReadSettings(WindowsProxySettings settings)
    {
        if (_disposed)
        {
            return ProxySettingsInitializationResult.ApplyFailure(_currentSettings);
        }

        try
        {
            _ = _clientPool.Update(settings);
            _currentSettings = _clientPool.CurrentSettings;
            return ProxySettingsInitializationResult.Loaded(settings);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return ProxySettingsInitializationResult.ApplyFailure(_currentSettings);
        }
    }

    private async Task<(PersistenceStatus Status, WindowsProxySettings? Settings)> ReadSettingsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await _settingsStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            return (GetReadStatus(read), read.Value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (PersistenceStatus.Cancelled, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return (PersistenceStatus.IoFailure, null);
        }
    }

    private async Task<PersistenceStatus> WriteSettingsAsync(
        WindowsProxySettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await _settingsStore.WriteAsync(settings, cancellationToken).ConfigureAwait(false)).Status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceStatus.Cancelled;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return PersistenceStatus.IoFailure;
        }
    }

    private void SetState(ProxySettingsInitializationResult result, bool initialized)
    {
        _initializationResult = result;
        _isInitialized = initialized;
        _statusMessage = result.StatusMessage;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetState(ProxySettingsSaveResult result)
    {
        _isInitialized = true;
        _statusMessage = result.StatusMessage;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task WritePersistenceDiagnosticAsync(
        DiagnosticEventId eventId,
        PersistenceStatus status)
    {
        try
        {
            await _diagnosticSink.WriteAsync(
                new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    DiagnosticLevelFor(status),
                    eventId,
                    ToDiagnosticOutcome(status),
                    ErrorCode: ToDiagnosticError(status)),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Proxy persistence remains usable when its best-effort diagnostic cannot be written.
        }
    }

    private static bool TryValidate(WindowsProxySettings? settings)
    {
        if (settings is null)
        {
            return false;
        }

        try
        {
            settings.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
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
/// Describes initial proxy settings loading without exposing custom URI text.
/// </summary>
internal sealed record ProxySettingsInitializationResult(
    ProxySettingsInitializationStage Stage,
    PersistenceStatus ReadStatus,
    WindowsProxySettings Settings)
{
    public PersistenceStatus DiagnosticStatus => Stage == ProxySettingsInitializationStage.ApplyFailed
        ? PersistenceStatus.IoFailure
        : ReadStatus;

    public string StatusMessage => Stage switch
    {
        ProxySettingsInitializationStage.Loaded => Settings.Mode switch
        {
            WindowsProxyMode.SystemDefault => AppStrings.Get("proxy.status.loaded.system_default"),
            WindowsProxyMode.CustomHttp => AppStrings.Get("proxy.status.loaded.custom_http"),
            WindowsProxyMode.Disabled => AppStrings.Get("proxy.status.loaded.disabled"),
            _ => AppStrings.Get("proxy.status.failed"),
        },
        ProxySettingsInitializationStage.NotFound => AppStrings.Get("proxy.status.not_found"),
        ProxySettingsInitializationStage.Cancelled => AppStrings.Get("proxy.status.cancelled"),
        ProxySettingsInitializationStage.ApplyFailed => AppStrings.Get("proxy.status.apply_failed"),
        ProxySettingsInitializationStage.ReadFailed when ReadStatus == PersistenceStatus.UnsupportedVersion =>
            AppStrings.Get("proxy.status.unsupported_version"),
        ProxySettingsInitializationStage.ReadFailed when ReadStatus is
            PersistenceStatus.InvalidData or PersistenceStatus.CorruptData =>
            AppStrings.Get("proxy.status.invalid"),
        _ => AppStrings.Get("proxy.status.failed"),
    };

    public static ProxySettingsInitializationResult Loading() => new(
        ProxySettingsInitializationStage.Loading,
        PersistenceStatus.NotFound,
        WindowsProxySettings.Default);

    public static ProxySettingsInitializationResult Loaded(WindowsProxySettings settings) => new(
        ProxySettingsInitializationStage.Loaded,
        PersistenceStatus.Succeeded,
        settings);

    public static ProxySettingsInitializationResult NotFound(WindowsProxySettings settings) => new(
        ProxySettingsInitializationStage.NotFound,
        PersistenceStatus.NotFound,
        settings);

    public static ProxySettingsInitializationResult ReadFailure(
        PersistenceStatus status,
        WindowsProxySettings settings) => new(
        status == PersistenceStatus.Cancelled
            ? ProxySettingsInitializationStage.Cancelled
            : ProxySettingsInitializationStage.ReadFailed,
        status,
        settings);

    public static ProxySettingsInitializationResult ApplyFailure(WindowsProxySettings settings) => new(
        ProxySettingsInitializationStage.ApplyFailed,
        PersistenceStatus.Succeeded,
        settings);

}

internal enum ProxySettingsInitializationStage
{
    Loading,
    Loaded,
    NotFound,
    Cancelled,
    ReadFailed,
    ApplyFailed,
}

/// <summary>
/// Describes a save outcome without exposing a custom proxy URI.
/// </summary>
internal sealed record ProxySettingsSaveResult(
    ProxySettingsSaveStage Stage,
    PersistenceStatus? PersistenceStatus = null,
    WindowsProxyMode? Mode = null)
{
    public string StatusMessage => Stage switch
    {
        ProxySettingsSaveStage.Completed => DescribeMode(Mode ?? WindowsProxyMode.SystemDefault),
        ProxySettingsSaveStage.Invalid => AppStrings.Get("proxy.save.invalid"),
        ProxySettingsSaveStage.WriteFailed when PersistenceStatus == global::TransDuck.Core.Persistence.PersistenceStatus.Cancelled =>
            AppStrings.Get("proxy.save.cancelled"),
        ProxySettingsSaveStage.WriteFailed => AppStrings.Get("proxy.save.failed"),
        ProxySettingsSaveStage.ApplyFailed => AppStrings.Get("proxy.save.apply_failed"),
        _ => AppStrings.Get("proxy.save.failed"),
    };

    public static ProxySettingsSaveResult Completed(WindowsProxyMode mode) => new(
        ProxySettingsSaveStage.Completed,
        global::TransDuck.Core.Persistence.PersistenceStatus.Succeeded,
        mode);

    public static ProxySettingsSaveResult Invalid() => new(ProxySettingsSaveStage.Invalid);

    public static ProxySettingsSaveResult WriteFailure(PersistenceStatus status) => new(
        ProxySettingsSaveStage.WriteFailed,
        status);

    public static ProxySettingsSaveResult ApplyFailed() => new(ProxySettingsSaveStage.ApplyFailed);

    private static string DescribeMode(WindowsProxyMode mode) => mode switch
    {
        WindowsProxyMode.SystemDefault => AppStrings.Get("proxy.save.completed.system_default"),
        WindowsProxyMode.CustomHttp => AppStrings.Get("proxy.save.completed.custom_http"),
        WindowsProxyMode.Disabled => AppStrings.Get("proxy.save.completed.disabled"),
        _ => AppStrings.Get("proxy.save.failed"),
    };
}

internal enum ProxySettingsSaveStage
{
    Completed,
    Invalid,
    WriteFailed,
    ApplyFailed,
}
