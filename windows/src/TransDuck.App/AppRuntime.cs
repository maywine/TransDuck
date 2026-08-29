using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using TransDuck.App.Services;
using TransDuck.App.Windows;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Capture;
using TransDuck.Platform.Windows.Clipboard;
using TransDuck.Platform.Windows.Hotkeys;
using TransDuck.Platform.Windows.Interop;
using TransDuck.Platform.Windows.Ocr;
using TransDuck.Platform.Windows.Persistence;
using TransDuck.Platform.Windows.Proxy;
using TransDuck.Platform.Windows.Selection;
using TransDuck.Platform.Windows.Startup;
using TransDuck.Platform.Windows.Translation;
using TransDuck.Platform.Windows.Tray;

namespace TransDuck.App;

/// <summary>
/// Composes the Windows MVP runtime, including native adapters, provider settings, and recovery paths.
/// </summary>
internal sealed class AppRuntime : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly NativeMessageWindow _hotkeyWindow = new(NativeWindowKind.MessageOnly);
    private readonly NativeMessageWindow _trayWindow = new(NativeWindowKind.HiddenTopLevel);
    private readonly ResultFloatingWindow _resultWindow = new();
    private readonly ShellNotifyIconTrayService _trayService;
    private readonly RegisterHotKeyService _hotkeyService;
    private readonly UiAutomationSelectionService _selectionService = new(new ClipboardCopyFallback());
    private readonly ScreenSelectionOverlay _selectionOverlay = new();
    private readonly IOcrService _ocrService = new TesseractOcrService();
    private readonly TranslationSessionController _translationController;
    private readonly JsonConfigurationStore _configurationStore;
    private readonly JsonProviderSettingsStore _providerSettingsStore;
    private readonly JsonHotkeySettingsStore _hotkeySettingsStore;
    private readonly JsonWindowsProxySettingsStore _proxySettingsStore;
    private readonly DpapiCredentialStore _credentialStore;
    private readonly JsonLinesHistoryStore _historyStore;
    private readonly JsonLinesDiagnosticSink _diagnosticSink;
    private readonly TranslationProviderRegistry _translationProviderRegistry = new();
    private readonly ProxyHttpClientPool _proxyHttpClientPool;
    private readonly ProxyTranslationHttpClientLeaseSource _translationClientLeaseSource;
    private readonly ProviderSettingsController _providerSettingsController;
    private readonly ProxySettingsController _proxySettingsController;
    private readonly HotkeySettingsController _hotkeySettingsController;
    private readonly StartupSettingsController _startupSettingsController;
    private readonly HistoryController _historyController;
    private readonly ContextMenu _trayMenu;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _lifecycleGate = new();
    private readonly object _trackedOperationsGate = new();
    private readonly HashSet<Task> _trackedOperations = [];
    private CancellationTokenSource? _operationCancellation;
    private WindowsGraphicsCaptureService? _captureService;
    private SettingsWindow? _settingsWindow;
    private HistoryWindow? _historyWindow;
    private TranslationRetrySnapshot? _retrySnapshot;
    private Task? _stopTask;
    private long _operationGeneration;
    private int _coreDisposed;
    private int _disposeRequested;
    private int _eventsDetached;
    private int _stopping;

    public AppRuntime()
    {
        _trayService = new ShellNotifyIconTrayService(_trayWindow, "TransDuck");
        _hotkeyService = new RegisterHotKeyService(_hotkeyWindow);
        var dataPaths = new WindowsDataPaths();
        _configurationStore = new JsonConfigurationStore(dataPaths);
        _providerSettingsStore = new JsonProviderSettingsStore(dataPaths);
        _hotkeySettingsStore = new JsonHotkeySettingsStore(dataPaths);
        _proxySettingsStore = new JsonWindowsProxySettingsStore(dataPaths);
        _credentialStore = new DpapiCredentialStore(dataPaths);
        _historyStore = new JsonLinesHistoryStore(dataPaths);
        _diagnosticSink = new JsonLinesDiagnosticSink(dataPaths);
        _proxyHttpClientPool = new ProxyHttpClientPool(WindowsProxySettings.Default);
        _translationClientLeaseSource = new ProxyTranslationHttpClientLeaseSource(_proxyHttpClientPool);
        _translationController = new TranslationSessionController(
            new OpenAiCompatibleSseClient(_translationClientLeaseSource));
        _providerSettingsController = new ProviderSettingsController(
            _providerSettingsStore,
            _configurationStore,
            _credentialStore,
            _diagnosticSink);
        _proxySettingsController = new ProxySettingsController(
            _proxySettingsStore,
            _proxyHttpClientPool,
            _diagnosticSink);
        _hotkeySettingsController = new HotkeySettingsController(
            _hotkeySettingsStore,
            _hotkeyService,
            _diagnosticSink);
        _startupSettingsController = new StartupSettingsController(
            new RegistryRunStartupRegistrationService(),
            _diagnosticSink);
        _historyController = new HistoryController(
            _configurationStore,
            _historyStore,
            _diagnosticSink);
        _translationProviderRegistry.Register(new OpenAiCompatibleProvider(_translationClientLeaseSource));
        _translationProviderRegistry.Register(new DeepLProvider(_translationClientLeaseSource));
        _translationProviderRegistry.Register(new OllamaProvider(_translationClientLeaseSource));
        _translationProviderRegistry.Register(new BingWebProvider(_translationClientLeaseSource));
        _translationProviderRegistry.Register(new GoogleWebProvider(_translationClientLeaseSource));
        _translationProviderRegistry.Register(new VolcengineProvider(_translationClientLeaseSource));
        _trayMenu = CreateTrayMenu();

        _trayService.PrimaryActionRequested += HandleTrayPrimaryActionRequested;
        _trayService.ContextMenuRequested += HandleTrayContextMenuRequested;
        _trayService.ExplorerRestarted += HandleExplorerRestarted;
        _hotkeyService.Pressed += HandleHotkeyPressed;
        _resultWindow.TranslationRequested += HandleTranslationRequested;
        _resultWindow.CaptureOcrRequested += HandleCaptureOcrRequested;
        _resultWindow.CancellationRequested += HandleCancellationRequested;
        _resultWindow.ResultCopyRequested += HandleResultCopyRequested;
        _resultWindow.RetryRequested += HandleRetryRequested;
        _hotkeySettingsController.StateChanged += HandleHotkeyStateChanged;
        SystemEvents.PowerModeChanged += HandlePowerModeChanged;
    }

    public Task StartAsync() => StartTrackedOperation(StartCoreAsync);

    private async Task StartCoreAsync()
    {
        if (IsDisposed || IsStopping || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        var proxy = await _proxySettingsController.InitializeAsync(_lifetimeCancellation.Token);
        if (IsDisposed || IsStopping || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        var trayResult = _trayService.Start();
        var hotkey = await _hotkeySettingsController.InitializeAsync(_lifetimeCancellation.Token);
        if (IsDisposed || IsStopping || _lifetimeCancellation.IsCancellationRequested)
        {
            return;
        }

        UpdateHotkeyPresentation();
        _resultWindow.SetStatus(AppStrings.Format(
            "runtime.start.status",
            AppStatusText.DescribeTrayStartResult(trayResult),
            hotkey.StatusMessage,
            proxy.StatusMessage));
    }

    public void Dispose()
    {
        BeginStopping();
        if (!HasTrackedOperations())
        {
            DisposeCore();
        }
    }

    public Task StopAsync()
    {
        lock (_lifecycleGate)
        {
            _stopTask ??= StopCoreAsync();
            return _stopTask;
        }
    }

    private async Task StopCoreAsync()
    {
        try
        {
            BeginStopping();
            await WaitForTrackedOperationsAsync();
        }
        finally
        {
            DisposeCore();
        }
    }

    private void BeginStopping()
    {
        lock (_trackedOperationsGate)
        {
            if (Volatile.Read(ref _stopping) != 0)
            {
                return;
            }

            Volatile.Write(ref _stopping, 1);
        }

        RunNonFatal(_lifetimeCancellation.Cancel);
        RunNonFatal(DetachEvents);
        RunNonFatal(() => CancelCurrentOperation(showStatus: false));
        RunNonFatal(CloseSettingsWindow);
        RunNonFatal(CloseHistoryWindow);
        RunNonFatal(() =>
        {
            _resultWindow.AllowFinalClose();
            if (_resultWindow.IsVisible)
            {
                _resultWindow.Close();
            }
        });
    }

    private void DetachEvents()
    {
        if (Interlocked.Exchange(ref _eventsDetached, 1) != 0)
        {
            return;
        }

        SystemEvents.PowerModeChanged -= HandlePowerModeChanged;
        _trayService.PrimaryActionRequested -= HandleTrayPrimaryActionRequested;
        _trayService.ContextMenuRequested -= HandleTrayContextMenuRequested;
        _trayService.ExplorerRestarted -= HandleExplorerRestarted;
        _hotkeyService.Pressed -= HandleHotkeyPressed;
        _resultWindow.TranslationRequested -= HandleTranslationRequested;
        _resultWindow.CaptureOcrRequested -= HandleCaptureOcrRequested;
        _resultWindow.CancellationRequested -= HandleCancellationRequested;
        _resultWindow.ResultCopyRequested -= HandleResultCopyRequested;
        _resultWindow.RetryRequested -= HandleRetryRequested;
        _hotkeySettingsController.StateChanged -= HandleHotkeyStateChanged;
    }

    private async Task WaitForTrackedOperationsAsync()
    {
        while (true)
        {
            Task[] operations;
            lock (_trackedOperationsGate)
            {
                if (_trackedOperations.Count == 0)
                {
                    return;
                }

                operations = [.. _trackedOperations];
            }

            try
            {
                await Task.WhenAll(operations);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // Each operation owns its visible failure state; stopping still waits for all continuations.
            }
        }
    }

    private bool HasTrackedOperations()
    {
        lock (_trackedOperationsGate)
        {
            return _trackedOperations.Count > 0;
        }
    }

    private Task StartTrackedOperation(Func<Task> operationFactory)
    {
        Task operation;
        lock (_trackedOperationsGate)
        {
            if (IsStopping || IsDisposed)
            {
                return Task.CompletedTask;
            }

            operation = operationFactory();
            _trackedOperations.Add(operation);
        }

        operation.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                {
                    _ = completed.Exception;
                }

                lock (_trackedOperationsGate)
                {
                    _trackedOperations.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return operation;
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _coreDisposed, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _disposeRequested, 1);
        var operationCancellation = _operationCancellation;
        _operationCancellation = null;
        DisposeNonFatal(operationCancellation);
        DisposeNonFatal(_captureService);
        DisposeNonFatal(_ocrService);
        DisposeNonFatal(_hotkeySettingsStore);
        DisposeNonFatal(_providerSettingsStore);
        DisposeNonFatal(_configurationStore);
        DisposeNonFatal(_credentialStore);
        DisposeNonFatal(_historyStore);
        DisposeNonFatal(_proxySettingsController);
        DisposeNonFatal(_proxySettingsStore);
        DisposeNonFatal(_proxyHttpClientPool);
        DisposeNonFatal(_startupSettingsController);
        DisposeNonFatal(_diagnosticSink);
        DisposeNonFatal(_hotkeyService);
        DisposeNonFatal(_trayService);
        DisposeNonFatal(_hotkeyWindow);
        DisposeNonFatal(_trayWindow);
        DisposeNonFatal(_lifetimeCancellation);
    }

    private static void DisposeNonFatal(IDisposable? disposable)
    {
        if (disposable is null)
        {
            return;
        }

        try
        {
            disposable.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Shutdown continues so independent native and persistence resources can still be released.
        }
    }

    private static void RunNonFatal(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Stopping continues after an independent window or event cleanup failure.
        }
    }

    private void HandleTrayPrimaryActionRequested(object? sender, EventArgs eventArgs) =>
        PostToUi(PresentInput);

    private void HandleTrayContextMenuRequested(object? sender, EventArgs eventArgs) =>
        PostToUi(ShowTrayMenu);

    private void HandleExplorerRestarted(object? sender, TrayOperationResult result) =>
        PostToUi(() => _resultWindow.SetStatus(AppStatusText.DescribeExplorerRestartResult(result)));

    private void HandleHotkeyPressed(object? sender, EventArgs eventArgs) =>
        PostToUi(() => _ = StartTrackedOperation(ReadSelectionAsync));

    private void HandleTranslationRequested(object? sender, string text) =>
        PostToUi(() => _ = StartTrackedOperation(() => TranslateAsync(text)));

    private void HandleCaptureOcrRequested(object? sender, string languageTag) =>
        PostToUi(() => _ = StartTrackedOperation(() => CaptureOcrAsync(languageTag)));

    private void HandleCancellationRequested(object? sender, EventArgs eventArgs) => CancelCurrentOperation();

    private void HandleResultCopyRequested(object? sender, string text) =>
        PostToUi(() => CopyResultToClipboard(text));

    private void HandleRetryRequested(object? sender, EventArgs eventArgs) =>
        PostToUi(RetryCurrentTranslation);

    private void HandleHotkeyStateChanged(object? sender, EventArgs eventArgs) =>
        PostToUi(UpdateHotkeyPresentation);

    private void CopyResultToClipboard(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _resultWindow.SetStatus(AppStrings.Get("runtime.copy.empty"));
            return;
        }

        try
        {
            Clipboard.SetText(text);
            _resultWindow.SetStatus(AppStrings.Get("runtime.copy.succeeded"));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            _resultWindow.SetStatus(AppStrings.Get("runtime.copy.failed"));
        }
    }

    private async Task ReadSelectionAsync()
    {
        var (operation, cancellationToken) = BeginOperation();
        try
        {
            var selection = await _selectionService.ReadAsync(cancellationToken);
            if (!IsCurrentOperation(operation))
            {
                return;
            }

            if (selection.Status == SelectionReadStatus.Succeeded && selection.Text is { } text)
            {
                PostCurrentOperationToUi(operation, () =>
                {
                    _resultWindow.Present(text);
                    _resultWindow.ClearResult();
                    _resultWindow.SetStatus(AppStatusText.DescribeSelectionSuccess(selection));
                });
                return;
            }

            PostCurrentOperationToUi(operation, () =>
            {
                _resultWindow.Present();
                _resultWindow.SetStatus(AppStatusText.DescribeSelectionFailure(selection));
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            PostCurrentOperationToUi(operation, () =>
            {
                _resultWindow.Present();
                _resultWindow.SetStatus(AppStrings.Get("selection.failure.exception"));
            });
        }
    }

    private async Task TranslateAsync(string text)
    {
        var (operation, cancellationToken) = BeginOperation();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatusForCurrentOperation(operation, AppStrings.Get("translation.input.empty"));
            return;
        }

        var requestId = Guid.NewGuid().ToString("N");
        var stopwatch = Stopwatch.StartNew();
        ProviderProfileSettings? profile = null;
        Configuration? configuration = null;
        var providerRequestStarted = false;
        try
        {
            using var settings = await _providerSettingsController.LoadForTranslationAsync(cancellationToken);
            if (!IsCurrentOperation(operation))
            {
                return;
            }

            if (!settings.Succeeded)
            {
                stopwatch.Stop();
                SetStatusForCurrentOperation(
                    operation,
                    AppStatusText.DescribeTranslationSettingsFailure(settings.Status));
                await WriteDiagnosticAsync(
                    DiagnosticEventId.TranslationFailed,
                    ToDiagnosticOutcome(settings.StorageStatus),
                    null,
                    requestId,
                    ToDiagnosticError(settings.StorageStatus),
                    stopwatch.ElapsedMilliseconds);
                return;
            }

            profile = settings.Profile!;
            configuration = settings.Configuration!;
            if (!_translationProviderRegistry.TryResolve(profile.Provider, out var provider) || provider is null)
            {
                stopwatch.Stop();
                SetStatusForCurrentOperation(operation, AppStrings.Get("translation.provider.unavailable"));
                await WriteDiagnosticAsync(
                    DiagnosticEventId.TranslationFailed,
                    DiagnosticOutcome.Failed,
                    profile.Provider.ProviderId,
                    requestId,
                    DiagnosticErrorCode.TranslationProviderUnavailable,
                    stopwatch.ElapsedMilliseconds);
                PostCurrentOperationToUi(operation, () => ApplyTerminalRetryState(
                    operation,
                    text,
                    TranslationSessionResult.Failed(
                        string.Empty,
                        QueryErrorCode.ProviderUnavailable,
                        retryable: false)));
                return;
            }

            var storedCredential = settings.Credential?.Reveal();
            var credentials = string.Equals(
                profile.Provider.ProviderId,
                TranslationProviderIds.Volcengine,
                StringComparison.Ordinal)
                ? VolcengineCredentialCodec.TryDecode(storedCredential, out var volcengineCredentials)
                    ? volcengineCredentials
                    : new TranslationCredentials(null)
                : new TranslationCredentials(storedCredential);
            // The request owns only transient strings; release DPAPI-backed bytes before streaming.
            settings.Dispose();

            var request = new TranslationProviderRequest(
                profile.Provider,
                profile.Endpoint,
                profile.Model,
                text,
                profile.SourceLanguage,
                profile.TargetLanguage,
                credentials,
                TimeSpan.FromSeconds(profile.TimeoutSeconds));
            _resultWindow.ClearResult();
            await WriteDiagnosticAsync(
                DiagnosticEventId.TranslationStarted,
                DiagnosticOutcome.Succeeded,
                profile.Provider.ProviderId,
                requestId,
                null,
                null);
            if (!IsCurrentOperation(operation))
            {
                stopwatch.Stop();
                await WriteDiagnosticAsync(
                    DiagnosticEventId.TranslationCancelled,
                    DiagnosticOutcome.Cancelled,
                    profile.Provider.ProviderId,
                    requestId,
                    null,
                    stopwatch.ElapsedMilliseconds);
                return;
            }

            providerRequestStarted = true;
            var terminal = await _translationController.RunAsync(
                provider,
                request,
                value => PostCurrentOperationToUi(operation, () => _resultWindow.SetResult(value)),
                value => PostCurrentOperationToUi(operation, () => _resultWindow.SetStatus(value)));
            stopwatch.Stop();
            await RecordTranslationTerminalAsync(
                requestId,
                text,
                profile,
                configuration,
                terminal,
                stopwatch.ElapsedMilliseconds);
            PostCurrentOperationToUi(operation, () => ApplyTerminalRetryState(operation, text, terminal));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            if (providerRequestStarted && profile is not null && configuration is not null)
            {
                var terminal = TranslationSessionResult.Cancelled(string.Empty);
                await RecordTranslationTerminalAsync(
                    requestId,
                    text,
                    profile,
                    configuration,
                    terminal,
                    stopwatch.ElapsedMilliseconds);
                PostCurrentOperationToUi(operation, () => ApplyTerminalRetryState(operation, text, terminal));
            }

            SetStatusForCurrentOperation(operation, AppStrings.Get("translation.status.cancelled"));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            stopwatch.Stop();
            if (providerRequestStarted && profile is not null && configuration is not null)
            {
                var terminal = TranslationSessionResult.Failed(
                    string.Empty,
                    QueryErrorCode.Internal,
                    retryable: false);
                await RecordTranslationTerminalAsync(
                    requestId,
                    text,
                    profile,
                    configuration,
                    terminal,
                    stopwatch.ElapsedMilliseconds);
                PostCurrentOperationToUi(operation, () => ApplyTerminalRetryState(operation, text, terminal));
            }
            else
            {
                await WriteDiagnosticAsync(
                    DiagnosticEventId.TranslationFailed,
                    DiagnosticOutcome.Failed,
                    profile?.Provider.ProviderId,
                    requestId,
                    DiagnosticErrorCode.TranslationInternal,
                    stopwatch.ElapsedMilliseconds);
                PostCurrentOperationToUi(operation, () => ApplyTerminalRetryState(
                    operation,
                    text,
                    TranslationSessionResult.Failed(
                        string.Empty,
                        QueryErrorCode.Internal,
                        retryable: false)));
            }

            SetStatusForCurrentOperation(operation, AppStrings.Get("translation.status.failed"));
        }
    }

    /// <summary>
    /// Records every provider-started terminal without permitting a newer UI generation
    /// to erase history or diagnostics.
    /// </summary>
    private async Task RecordTranslationTerminalAsync(
        string requestId,
        string text,
        ProviderProfileSettings profile,
        Configuration configuration,
        TranslationSessionResult terminal,
        long durationMilliseconds)
    {
        await AppendTranslationHistoryAsync(
            requestId,
            text,
            profile,
            configuration,
            terminal);
        await WriteDiagnosticAsync(
            terminal.TerminalKind switch
            {
                TranslationStreamEventKind.Completed => DiagnosticEventId.TranslationCompleted,
                TranslationStreamEventKind.Cancelled => DiagnosticEventId.TranslationCancelled,
                _ => DiagnosticEventId.TranslationFailed,
            },
            terminal.TerminalKind switch
            {
                TranslationStreamEventKind.Completed => DiagnosticOutcome.Succeeded,
                TranslationStreamEventKind.Cancelled => DiagnosticOutcome.Cancelled,
                _ => DiagnosticOutcome.Failed,
            },
            profile.Provider.ProviderId,
            requestId,
            ToDiagnosticError(terminal.ErrorCode),
            durationMilliseconds);
    }

    private async Task AppendTranslationHistoryAsync(
        string requestId,
        string text,
        ProviderProfileSettings profile,
        Configuration configuration,
        TranslationSessionResult terminal)
    {
        try
        {
            var request = new QueryRequest(
                SchemaVersion: 1,
                RequestId: requestId,
                QueryKind: QueryKind.Translation,
                Text: text,
                SourceLanguage: profile.SourceLanguage,
                TargetLanguage: profile.TargetLanguage,
                Provider: profile.Provider);
            var result = terminal.TerminalKind switch
            {
                TranslationStreamEventKind.Completed => new QueryResult(
                    1,
                    requestId,
                    QueryKind.Translation,
                    profile.Provider,
                    QueryTerminalState.Completed,
                    profile.SourceLanguage,
                    profile.TargetLanguage,
                    new QueryResultPayload(terminal.Text)),
                TranslationStreamEventKind.Cancelled => new QueryResult(
                    1,
                    requestId,
                    QueryKind.Translation,
                    profile.Provider,
                    QueryTerminalState.Cancelled,
                    profile.SourceLanguage,
                    profile.TargetLanguage),
                _ => new QueryResult(
                    1,
                    requestId,
                    QueryKind.Translation,
                    profile.Provider,
                    QueryTerminalState.Failed,
                    profile.SourceLanguage,
                    profile.TargetLanguage,
                    Error: new QueryError(
                        terminal.ErrorCode ?? QueryErrorCode.Internal,
                        AppStatusText.DescribeTranslationFailure(terminal.ErrorCode ?? QueryErrorCode.Internal),
                        terminal.Retryable)),
            };
            var append = await _historyStore.AppendAsync(
                new HistoryEntry(1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, request, result),
                configuration.HistoryRetention,
                CancellationToken.None);
            if (append.Succeeded)
            {
                await WriteDiagnosticAsync(
                    DiagnosticEventId.HistoryAppend,
                    DiagnosticOutcome.Succeeded,
                    profile.Provider.ProviderId,
                    requestId,
                    null,
                    null);
            }
            else
            {
                await WriteDiagnosticAsync(
                    DiagnosticEventId.HistoryAppend,
                    append.Status == PersistenceStatus.Cancelled
                        ? DiagnosticOutcome.Cancelled
                        : DiagnosticOutcome.Failed,
                    profile.Provider.ProviderId,
                    requestId,
                    ToDiagnosticError(append.Status),
                    null);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            await WriteDiagnosticAsync(
                DiagnosticEventId.HistoryAppend,
                DiagnosticOutcome.Failed,
                profile.Provider.ProviderId,
                requestId,
                DiagnosticErrorCode.IoFailure,
                null);
        }
    }

    private async Task WriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticOutcome outcome,
        string? providerId,
        string? requestId,
        DiagnosticErrorCode? errorCode,
        long? durationMilliseconds)
    {
        try
        {
            await _diagnosticSink.WriteAsync(
                new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    outcome == DiagnosticOutcome.Failed ? DiagnosticLevel.Error : DiagnosticLevel.Information,
                    eventId,
                    outcome,
                    requestId,
                    providerId,
                    errorCode,
                    durationMilliseconds),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Diagnostics cannot change the primary translation result.
        }
    }

    private static DiagnosticErrorCode? ToDiagnosticError(PersistenceStatus status) => status switch
    {
        PersistenceStatus.InvalidData => DiagnosticErrorCode.InvalidData,
        PersistenceStatus.UnsupportedVersion => DiagnosticErrorCode.UnsupportedVersion,
        PersistenceStatus.CorruptData => DiagnosticErrorCode.CorruptData,
        PersistenceStatus.IoFailure => DiagnosticErrorCode.IoFailure,
        _ => null,
    };

    private static DiagnosticErrorCode? ToDiagnosticError(PersistenceStatus? status) =>
        status is { } value ? ToDiagnosticError(value) : null;

    private static DiagnosticOutcome ToDiagnosticOutcome(PersistenceStatus? status) => status switch
    {
        PersistenceStatus.Succeeded => DiagnosticOutcome.Succeeded,
        PersistenceStatus.NotFound => DiagnosticOutcome.NotFound,
        PersistenceStatus.Cancelled => DiagnosticOutcome.Cancelled,
        _ => DiagnosticOutcome.Failed,
    };

    private static DiagnosticErrorCode? ToDiagnosticError(QueryErrorCode? errorCode) => errorCode switch
    {
        QueryErrorCode.InvalidRequest => DiagnosticErrorCode.TranslationInvalidRequest,
        QueryErrorCode.ProviderUnavailable => DiagnosticErrorCode.TranslationProviderUnavailable,
        QueryErrorCode.Timeout => DiagnosticErrorCode.TranslationTimeout,
        QueryErrorCode.Network => DiagnosticErrorCode.TranslationNetwork,
        QueryErrorCode.Authentication => DiagnosticErrorCode.TranslationAuthentication,
        QueryErrorCode.RateLimited => DiagnosticErrorCode.TranslationRateLimited,
        QueryErrorCode.UnsupportedLanguage => DiagnosticErrorCode.TranslationUnsupportedLanguage,
        QueryErrorCode.Internal => DiagnosticErrorCode.TranslationInternal,
        _ => null,
    };

    private async Task CaptureOcrAsync(string languageTag)
    {
        var (operation, cancellationToken) = BeginOperation();
        _resultWindow.Hide();
        try
        {
            var selection = await _selectionOverlay.SelectAsync(cancellationToken);
            if (!IsCurrentOperation(operation))
            {
                return;
            }

            if (selection is null)
            {
                PostCurrentOperationToUi(operation, () =>
                {
                    _resultWindow.Present();
                    _resultWindow.SetStatus(AppStrings.Get("capture.status.cancelled"));
                });
                return;
            }

            // Let all overlay windows leave the compositor, then discard the first capture frame.
            await _dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
            var capture = (_captureService ??= new WindowsGraphicsCaptureService());
            var captureResult = await capture.CaptureAsync(
                selection,
                cancellationToken,
                framesToDiscard: 1);
            if (!IsCurrentOperation(operation))
            {
                captureResult.Bitmap?.Dispose();
                return;
            }

            if (captureResult.Status != ScreenCaptureStatus.Succeeded || captureResult.Bitmap is null)
            {
                PostCurrentOperationToUi(operation, () =>
                {
                    _resultWindow.Present();
                    _resultWindow.SetStatus(AppStatusText.DescribeCaptureStatus(captureResult.Status));
                });
                return;
            }

            using (captureResult.Bitmap)
            {
                var ocrResult = await _ocrService.RecognizeAsync(
                    captureResult.Bitmap,
                    languageTag,
                    cancellationToken);
                if (!IsCurrentOperation(operation))
                {
                    return;
                }

                PostCurrentOperationToUi(operation, () =>
                {
                    _resultWindow.Present(ocrResult.Text);
                    _resultWindow.SetResult(ocrResult.Text ?? string.Empty);
                    _resultWindow.SetStatus(AppStatusText.DescribeOcrStatus(ocrResult.Status));
                });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PostCurrentOperationToUi(operation, () =>
            {
                _resultWindow.Present();
                _resultWindow.SetStatus(AppStrings.Get("capture.status.cancelled"));
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            PostCurrentOperationToUi(operation, () =>
            {
                _resultWindow.Present();
                _resultWindow.SetStatus(AppStrings.Get("capture.status.exception"));
            });
        }
    }

    private void HandlePowerModeChanged(object? sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode != PowerModes.Resume)
        {
            return;
        }

        PostToUi(() =>
        {
            try
            {
                var result = _hotkeyService.RestoreAfterPowerResume();
                _resultWindow.SetStatus(AppStatusText.DescribeHotkeyResult(result));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                _resultWindow.SetStatus(AppStrings.Get("hotkey.resume.failed"));
            }
            finally
            {
                UpdateHotkeyPresentation();
            }
        });
    }

    private (long Operation, CancellationToken Token) BeginOperation()
    {
        ClearRetryState();
        var nextOperation = Interlocked.Increment(ref _operationGeneration);
        var previousCancellation = _operationCancellation;
        _translationController.CancelCurrent();
        previousCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        previousCancellation?.Dispose();
        return (nextOperation, cancellation.Token);
    }

    private bool IsCurrentOperation(long operation) =>
        Interlocked.Read(ref _operationGeneration) == operation;

    private void CancelCurrentOperation(bool showStatus = true)
    {
        ClearRetryState();
        Interlocked.Increment(ref _operationGeneration);
        _translationController.CancelCurrent();
        _operationCancellation?.Cancel();
        if (showStatus && !IsDisposed && !IsStopping)
        {
            _resultWindow.SetStatus(AppStrings.Get("runtime.operation.cancelled"));
        }
    }

    private void PresentInput()
    {
        _resultWindow.Present();
        _resultWindow.SetStatus(_hotkeySettingsController.IsRegistrationActive
            ? AppStrings.Format("runtime.input.prompt.active", _hotkeySettingsController.CurrentHotkeyText)
            : AppStrings.Get("runtime.input.prompt.unavailable"));
    }

    private ContextMenu CreateTrayMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        menu.Items.Add(CreateMenuItem(
            "OpenInputTrayMenuItem",
            AppStrings.Get("runtime.menu.open_input"),
            PresentInput));
        menu.Items.Add(CreateMenuItem(
            "SettingsTrayMenuItem",
            AppStrings.Get("runtime.menu.settings"),
            ShowSettings));
        menu.Items.Add(CreateMenuItem(
            "HistoryTrayMenuItem",
            AppStrings.Get("runtime.menu.history"),
            ShowHistory));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateMenuItem(
            "ExitTrayMenuItem",
            AppStrings.Get("runtime.menu.exit"),
            ExitApplication));
        return menu;
    }

    private void ShowTrayMenu()
    {
        // Shell notification-area menus require their hidden top-level owner to be foreground first.
        _trayService.TryActivateContextMenuOwner();
        // An unseen result window has no presentation source and cannot anchor the first tray menu.
        _trayMenu.IsOpen = true;
    }

    private void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            var settingsWindow = new SettingsWindow(
                _providerSettingsController,
                _proxySettingsController,
                _hotkeySettingsController,
                _startupSettingsController);
            settingsWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_settingsWindow, settingsWindow))
                {
                    _settingsWindow = null;
                }
            };
            _settingsWindow = settingsWindow;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void CloseSettingsWindow()
    {
        var settingsWindow = _settingsWindow;
        _settingsWindow = null;
        settingsWindow?.Close();
    }

    private void ShowHistory()
    {
        if (_historyWindow is null)
        {
            var historyWindow = new HistoryWindow(_historyController);
            historyWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_historyWindow, historyWindow))
                {
                    _historyWindow = null;
                }
            };
            _historyWindow = historyWindow;
        }

        _historyWindow.Show();
        _historyWindow.Activate();
    }

    private void CloseHistoryWindow()
    {
        var historyWindow = _historyWindow;
        _historyWindow = null;
        historyWindow?.Close();
    }

    private bool IsDisposed => Volatile.Read(ref _disposeRequested) != 0;

    private bool IsStopping => Volatile.Read(ref _stopping) != 0;

    private void UpdateHotkeyPresentation() => _resultWindow.SetSelectionHotkeyHint(
        _hotkeySettingsController.IsRegistrationActive
            ? _hotkeySettingsController.CurrentHotkeyText
            : null);

    private void RetryCurrentTranslation()
    {
        if (_retrySnapshot is not { } snapshot)
        {
            return;
        }

        ClearRetryState();
        _ = StartTrackedOperation(() => TranslateAsync(snapshot.SourceText));
    }

    private void ApplyTerminalRetryState(
        long operation,
        string sourceText,
        TranslationSessionResult terminal)
    {
        if (!IsCurrentOperation(operation))
        {
            return;
        }

        if (terminal.TerminalKind != TranslationStreamEventKind.Failed)
        {
            ClearRetryState();
            return;
        }

        var errorCode = terminal.ErrorCode ?? QueryErrorCode.Internal;
        _resultWindow.ShowTranslationErrorCode(AppStatusText.DescribeTranslationErrorCode(errorCode));
        if (terminal.Retryable && IsRetryableError(errorCode))
        {
            _retrySnapshot = new TranslationRetrySnapshot(sourceText);
            _resultWindow.SetRetryEnabled(true);
            return;
        }

        _retrySnapshot = null;
        _resultWindow.SetRetryEnabled(false);
    }

    private void ClearRetryState()
    {
        _retrySnapshot = null;
        if (!IsDisposed && !IsStopping)
        {
            _resultWindow.SetRetryEnabled(false);
            _resultWindow.ClearTranslationErrorCode();
        }
    }

    private static bool IsRetryableError(QueryErrorCode errorCode) => errorCode is
        QueryErrorCode.Timeout or
        QueryErrorCode.Network or
        QueryErrorCode.RateLimited or
        QueryErrorCode.ProviderUnavailable;

    private sealed record TranslationRetrySnapshot(string SourceText);

    private void PostCurrentOperationToUi(long operation, Action action) =>
        PostToUi(() =>
        {
            if (IsCurrentOperation(operation))
            {
                action();
            }
        });

    private void SetStatusForCurrentOperation(long operation, string status) =>
        PostCurrentOperationToUi(operation, () => _resultWindow.SetStatus(status));

    /// <summary>
    /// Marshals native and provider callbacks to the application dispatcher while
    /// shutdown remains non-observable.
    /// </summary>
    private void PostToUi(Action action)
    {
        if (IsDisposed || IsStopping || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            if (!IsDisposed && !IsStopping)
            {
                action();
            }

            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                if (!IsDisposed && !IsStopping)
                {
                    action();
                }
            }));
        }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            // Dispatcher shutdown races must not revive native services or touch WPF windows.
        }
    }

    private async void ExitApplication()
    {
        try
        {
            await StopAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Shutdown continues after a nonfatal stopping failure.
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    private static MenuItem CreateMenuItem(string automationId, string label, Action action)
    {
        var item = new MenuItem { Header = label };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => action();
        return item;
    }

}
