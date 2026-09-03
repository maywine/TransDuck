// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Startup;

namespace TransDuck.App.Services;

/// <summary>
/// Coordinates the user-visible ZIP sign-in preference while keeping registry state and exception details outside the UI.
/// </summary>
internal sealed class StartupSettingsController : IDisposable
{
    private readonly IStartupRegistrationService _startupService;
    private readonly IDiagnosticSink _diagnosticSink;
    private StartupRegistrationResult _currentState = StartupRegistrationResult.Unavailable();
    private string _statusMessage = AppStrings.Get("startup.status.loading");
    private bool _isInitialized;
    private bool _disposed;

    public StartupSettingsController(
        IStartupRegistrationService startupService,
        IDiagnosticSink diagnosticSink)
    {
        _startupService = startupService;
        _diagnosticSink = diagnosticSink;
    }

    public event EventHandler? StateChanged;

    public StartupRegistrationResult CurrentState => _currentState;

    public bool IsEnabled => _currentState.IsEnabled;

    public bool IsInitialized => _isInitialized;

    public string StatusMessage => _statusMessage;

    public async Task<StartupRegistrationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _disposed ? StartupRegistrationResult.Failed() : _startupService.GetStatus();
        cancellationToken.ThrowIfCancellationRequested();
        await WriteDiagnosticAsync(result).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SetState(result, initialized: true);
        return result;
    }

    public async Task<StartupRegistrationResult> SetEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _disposed
            ? StartupRegistrationResult.Failed()
            : isEnabled ? _startupService.Enable() : _startupService.Disable();
        cancellationToken.ThrowIfCancellationRequested();
        await WriteDiagnosticAsync(result).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        SetState(result, initialized: true);
        return result;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _startupService.Dispose();
    }

    private void SetState(StartupRegistrationResult result, bool initialized)
    {
        _currentState = result;
        _isInitialized = initialized;
        _statusMessage = DescribeStatus(result.Status);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task WriteDiagnosticAsync(StartupRegistrationResult result)
    {
        var (level, outcome, errorCode) = result.Status switch
        {
            StartupRegistrationStatus.Enabled =>
                (DiagnosticLevel.Information, DiagnosticOutcome.Succeeded, (DiagnosticErrorCode?)null),
            StartupRegistrationStatus.Disabled =>
                (DiagnosticLevel.Information, DiagnosticOutcome.NotFound, (DiagnosticErrorCode?)null),
            StartupRegistrationStatus.Stale =>
                (DiagnosticLevel.Warning, DiagnosticOutcome.NotFound, (DiagnosticErrorCode?)null),
            StartupRegistrationStatus.Conflict =>
                (DiagnosticLevel.Warning, DiagnosticOutcome.Failed, DiagnosticErrorCode.StartupRegistrationFailure),
            StartupRegistrationStatus.Unavailable =>
                (DiagnosticLevel.Warning, DiagnosticOutcome.Failed, DiagnosticErrorCode.StartupRegistrationFailure),
            _ => (DiagnosticLevel.Error, DiagnosticOutcome.Failed, DiagnosticErrorCode.StartupRegistrationFailure),
        };

        try
        {
            await _diagnosticSink.WriteAsync(
                new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    level,
                    DiagnosticEventId.StartupRegistration,
                    outcome,
                    ErrorCode: errorCode),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Startup registration remains usable even when the best-effort diagnostic sink is unavailable.
        }
    }

    private static string DescribeStatus(StartupRegistrationStatus status) => status switch
    {
        StartupRegistrationStatus.Enabled => AppStrings.Get("startup.status.enabled"),
        StartupRegistrationStatus.Disabled => AppStrings.Get("startup.status.disabled"),
        StartupRegistrationStatus.Stale => AppStrings.Get("startup.status.stale"),
        StartupRegistrationStatus.Conflict => AppStrings.Get("startup.status.conflict"),
        StartupRegistrationStatus.Unavailable => AppStrings.Get("startup.status.unavailable"),
        _ => AppStrings.Get("startup.status.failed"),
    };
}
