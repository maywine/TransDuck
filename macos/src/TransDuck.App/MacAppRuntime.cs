using System.Diagnostics;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Persistence;
using TransDuck.Infrastructure.Proxy;
using TransDuck.Infrastructure.Translation;
using TransDuck.Platform.MacOS.Capture;
using TransDuck.Platform.MacOS.Hotkeys;
using TransDuck.Platform.MacOS.Ocr;
using TransDuck.Platform.MacOS.Persistence;
using TransDuck.Platform.MacOS.Selection;
using TransDuck.Platform.MacOS.Startup;

namespace TransDuck.MacOS.App;

internal sealed class MacAppRuntime : IAsyncDisposable
{
    private static readonly HistoryRetention DefaultRetention = new(100, 30);
    private readonly MacDataPaths _dataPaths = new();
    private readonly JsonConfigurationStore _configurationStore;
    private readonly JsonProviderSettingsStore _providerSettingsStore;
    private readonly JsonProxySettingsStore _proxySettingsStore;
    private readonly JsonMacHotkeySettingsStore _hotkeySettingsStore;
    private readonly MacKeychainCredentialStore _credentialStore = new();
    private readonly JsonLinesHistoryStore _historyStore;
    private readonly JsonLinesDiagnosticSink _diagnosticSink;
    private readonly ProxyHttpClientPool _httpClientPool = new(ProxySettings.Default);
    private readonly TranslationProviderRegistry _providers = new();
    private readonly MacAccessibilitySelectionService _selectionService = new();
    private readonly MacScreenCaptureService _captureService = new();
    private readonly VisionOcrService _ocrService = new();
    private readonly LaunchAgentStartupService _startupService = new();
    private readonly MacGlobalHotkeyService _hotkeyService = new(new SharpHookKeyboardBackend());
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _stateGate = new();
    private readonly object _operationGate = new();
    private readonly object _trackedOperationsGate = new();
    private readonly HashSet<Task> _trackedOperations = [];
    private MacRuntimeState _state = new(string.Empty, string.Empty, "Starting TransDuck...", false, false);
    private CancellationTokenSource? _operationCancellation;
    private RetrySnapshot? _retry;
    private long _operationGeneration;
    private bool _hotkeyStarted;
    private int _disposeRequested;

    public MacAppRuntime()
    {
        if (OperatingSystem.IsMacOS())
        {
            _dataPaths.EnsureRootDirectory();
        }

        _configurationStore = new JsonConfigurationStore(_dataPaths);
        _providerSettingsStore = new JsonProviderSettingsStore(_dataPaths);
        _proxySettingsStore = new JsonProxySettingsStore(_dataPaths);
        _hotkeySettingsStore = new JsonMacHotkeySettingsStore(_dataPaths);
        _historyStore = new JsonLinesHistoryStore(_dataPaths);
        _diagnosticSink = new JsonLinesDiagnosticSink(_dataPaths);
        var leaseSource = new ProxyTranslationHttpClientLeaseSource(_httpClientPool);
        _providers.Register(new OpenAiCompatibleProvider(leaseSource));
        _providers.Register(new DeepLProvider(leaseSource));
        _providers.Register(new OllamaProvider(leaseSource));
        _providers.Register(new BingWebProvider(leaseSource));
        _providers.Register(new GoogleWebProvider(leaseSource));
        _providers.Register(new VolcengineProvider(leaseSource));
        _hotkeyService.Pressed += HandleHotkeyPressed;
    }

    public static IReadOnlyList<ProviderDefinition> ProviderDefinitions { get; } =
    [
        new(
            TranslationProviderIds.OpenAiCompatible,
            "OpenAI-compatible",
            "https://api.openai.com/v1/chat/completions",
            ModelRequired: true,
            ProviderCredentialKind.ApiKey),
        new(
            TranslationProviderIds.DeepL,
            "DeepL",
            "https://api-free.deepl.com/v2/translate",
            ModelRequired: false,
            ProviderCredentialKind.ApiKey),
        new(
            TranslationProviderIds.Ollama,
            "Ollama",
            "http://localhost:11434/api/chat",
            ModelRequired: true,
            ProviderCredentialKind.Optional),
        new(
            TranslationProviderIds.Bing,
            "Bing (unofficial web)",
            BingWebProvider.DefaultEndpoint,
            ModelRequired: false,
            ProviderCredentialKind.Optional),
        new(
            TranslationProviderIds.Google,
            "Google (unofficial web)",
            GoogleWebProvider.DefaultEndpoint,
            ModelRequired: false,
            ProviderCredentialKind.None),
        new(
            TranslationProviderIds.Volcengine,
            "Volcengine Translate",
            VolcengineProvider.DefaultEndpoint,
            ModelRequired: false,
            ProviderCredentialKind.VolcenginePair),
    ];

    public event EventHandler<MacRuntimeState>? StateChanged;

    public event EventHandler? PresentationRequested;

    public MacRuntimeState State
    {
        get
        {
            lock (_stateGate)
            {
                return _state;
            }
        }
    }

    internal void ReportStartupFailure() => PublishState(
        status: "TransDuck could not finish startup initialization.",
        isBusy: false);

    public Task InitializeAsync() => TrackOperation(InitializeCoreAsync);

    private async Task InitializeCoreAsync()
    {
        var cancellationToken = _lifetimeCancellation.Token;
        var status = new List<string>();
        var proxyRead = await _proxySettingsStore.ReadAsync(cancellationToken);
        if (proxyRead.Succeeded)
        {
            try
            {
                _httpClientPool.Update(proxyRead.Value!);
                status.Add("proxy settings loaded");
            }
            catch (ArgumentException)
            {
                status.Add("proxy settings invalid; using system default");
            }
        }
        else if (proxyRead.Status != PersistenceStatus.NotFound)
        {
            status.Add("proxy settings unavailable; using system default");
        }

        var hotkeyRead = await _hotkeySettingsStore.ReadAsync(cancellationToken);
        var hotkey = hotkeyRead.Succeeded ? hotkeyRead.Value! : MacHotkeySettings.Default;
        if (_selectionService.EnsurePermission(prompt: false))
        {
            var hotkeyStatus = await _hotkeyService.StartAsync(hotkey, cancellationToken);
            _hotkeyStarted = hotkeyStatus == MacGlobalHotkeyStatus.Registered;
            status.Add(_hotkeyStarted ? "global hotkey ready" : "global hotkey unavailable");
        }
        else
        {
            _hotkeyService.TrySetSettings(hotkey);
            status.Add("Accessibility permission is required for selected-text translation");
        }

        var configuration = await _configurationStore.ReadAsync(cancellationToken);
        var profiles = await _providerSettingsStore.ReadAsync(cancellationToken);
        if (!configuration.Succeeded || !profiles.Succeeded || profiles.Value!.Profiles.Count == 0)
        {
            status.Add("open Settings to configure a translation provider");
        }

        PublishState(status: string.Join("; ", status) + ".");
    }

    public Task TranslateAsync(string text) =>
        TrackOperation(() => TranslateAsync(text, QueryKind.Translation));

    public Task TranslateSelectedTextAsync(bool promptForPermission) =>
        TrackOperation(() => TranslateSelectedTextCoreAsync(promptForPermission));

    private async Task TranslateSelectedTextCoreAsync(bool promptForPermission)
    {
        PresentationRequested?.Invoke(this, EventArgs.Empty);
        if (promptForPermission && !_hotkeyStarted)
        {
            await EnsureAccessibilityAndHotkeyAsync(prompt: true);
        }

        var selection = _selectionService.ReadSelectedText(promptForPermission: false);
        if (!selection.Succeeded)
        {
            PublishState(status: DescribeSelectionFailure(selection.Status), isBusy: false);
            return;
        }

        await TranslateAsync(selection.Text!, QueryKind.Translation);
    }

    public Task CaptureOcrAndTranslateAsync(string languageTag) =>
        TrackOperation(() => CaptureOcrAndTranslateCoreAsync(languageTag));

    private async Task CaptureOcrAndTranslateCoreAsync(string languageTag)
    {
        PresentationRequested?.Invoke(this, EventArgs.Empty);
        var (generation, cancellationToken) = BeginOperation();
        PublishCurrentState(generation, status: "Select a screen region...", isBusy: true);
        using var capture = await _captureService.CaptureRegionAsync(cancellationToken);
        if (!IsCurrent(generation))
        {
            return;
        }

        if (!capture.Succeeded)
        {
            PublishCurrentState(
                generation,
                status: capture.Status == MacScreenCaptureStatus.Cancelled
                    ? "Screen capture cancelled."
                    : capture.Status == MacScreenCaptureStatus.PermissionRequired
                        ? "Grant Screen Recording permission in System Settings, then try again."
                    : "Screen capture failed. Check Screen Recording permission.",
                isBusy: false);
            return;
        }

        PublishCurrentState(generation, status: "Recognizing text locally with macOS Vision...", isBusy: true);
        var ocr = await _ocrService.RecognizeAsync(capture.ImagePath!, languageTag, cancellationToken);
        if (!IsCurrent(generation))
        {
            return;
        }

        if (!ocr.Succeeded)
        {
            PublishCurrentState(generation, status: DescribeOcrFailure(ocr.Status), isBusy: false);
            return;
        }

        await TranslateAsync(ocr.Text!, QueryKind.Ocr, generation, cancellationToken);
    }

    public void CancelCurrentOperation()
    {
        lock (_operationGate)
        {
            _operationGeneration++;
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation = null;
        }

        PublishState(status: "Operation cancelled.", isBusy: false);
    }

    public Task RetryAsync()
    {
        RetrySnapshot? retry;
        lock (_stateGate)
        {
            retry = _retry;
        }

        return retry is null ? Task.CompletedTask : TranslateAsync(retry.Text, retry.QueryKind);
    }

    public Task<MacSettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken) =>
        TrackOperation(() => LoadSettingsCoreAsync(cancellationToken));

    private async Task<MacSettingsSnapshot> LoadSettingsCoreAsync(CancellationToken cancellationToken)
    {
        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken);
        var configurationRead = await _configurationStore.ReadAsync(cancellationToken);
        var proxyRead = await _proxySettingsStore.ReadAsync(cancellationToken);
        var hotkeyRead = await _hotkeySettingsStore.ReadAsync(cancellationToken);
        var configuration = configurationRead.Succeeded
            ? configurationRead.Value!
            : DefaultConfiguration();
        return new MacSettingsSnapshot(
            configuration,
            providerRead.Succeeded ? providerRead.Value!.Profiles : [],
            proxyRead.Succeeded ? proxyRead.Value! : _httpClientPool.CurrentSettings,
            hotkeyRead.Succeeded ? hotkeyRead.Value! : _hotkeyService.Settings,
            _startupService.GetStatus(),
            providerRead.Status,
            configurationRead.Status,
            proxyRead.Status,
            hotkeyRead.Status);
    }

    public Task<PersistenceStatus> GetCredentialStatusAsync(
        string providerId,
        CancellationToken cancellationToken) =>
        TrackOperation(() => GetCredentialStatusCoreAsync(providerId, cancellationToken));

    private async Task<PersistenceStatus> GetCredentialStatusCoreAsync(
        string providerId,
        CancellationToken cancellationToken)
    {
        var definition = FindProviderDefinition(providerId);
        if (definition is null || definition.CredentialKind == ProviderCredentialKind.None)
        {
            return PersistenceStatus.NotFound;
        }

        var read = await _credentialStore.GetAsync(
            new CredentialKey(providerId),
            cancellationToken);
        read.Value?.Dispose();
        return read.Status;
    }

    public Task<MacSettingsSaveResult> SaveSettingsAsync(
        MacSettingsInput input,
        CancellationToken cancellationToken) =>
        TrackOperation(() => SaveSettingsCoreAsync(input, cancellationToken));

    private async Task<MacSettingsSaveResult> SaveSettingsCoreAsync(
        MacSettingsInput input,
        CancellationToken cancellationToken)
    {
        if (!TryValidateSettingsInput(input, out var profile, out var error))
        {
            return new MacSettingsSaveResult(false, error!);
        }

        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken);
        if (providerRead.Status is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound))
        {
            return new MacSettingsSaveResult(false, "Provider settings could not be read.");
        }

        var profiles = providerRead.Succeeded
            ? providerRead.Value!.Profiles.Where(candidate =>
                !string.Equals(candidate.Provider.ProviderId, input.ProviderId, StringComparison.Ordinal)).ToList()
            : [];
        profiles.Add(profile!);
        var providerWrite = await _providerSettingsStore.WriteAsync(
            new ProviderSettingsDocument(ProviderSettingsMigration.CurrentVersion, profiles),
            cancellationToken);
        if (!providerWrite.Succeeded)
        {
            return new MacSettingsSaveResult(false, "Provider settings could not be saved.");
        }

        var configuration = new Configuration(
            1,
            ConfigurationMigration.CurrentVersion,
            profile!.Provider,
            input.HistoryRetention);
        var configurationWrite = await _configurationStore.WriteAsync(configuration, cancellationToken);
        if (!configurationWrite.Succeeded)
        {
            return new MacSettingsSaveResult(false, "General settings could not be saved.");
        }

        var credentialResult = await SaveCredentialAsync(input, cancellationToken);
        if (credentialResult is not null)
        {
            return credentialResult;
        }

        var proxyWrite = await _proxySettingsStore.WriteAsync(input.ProxySettings, cancellationToken);
        if (!proxyWrite.Succeeded)
        {
            return new MacSettingsSaveResult(false, "Proxy settings could not be saved.");
        }

        try
        {
            _httpClientPool.Update(input.ProxySettings);
        }
        catch (ArgumentException)
        {
            return new MacSettingsSaveResult(false, "Proxy settings were saved but could not be applied.");
        }

        var hotkeyWrite = await _hotkeySettingsStore.WriteAsync(input.HotkeySettings, cancellationToken);
        if (!hotkeyWrite.Succeeded || !_hotkeyService.TrySetSettings(input.HotkeySettings))
        {
            return new MacSettingsSaveResult(false, "Hotkey settings could not be saved.");
        }

        var startup = input.StartAtLogin
            ? await _startupService.EnableAsync(cancellationToken)
            : await _startupService.DisableAsync(cancellationToken);
        if (startup.Status is MacStartupStatus.Conflict or MacStartupStatus.Failed)
        {
            return new MacSettingsSaveResult(
                false,
                "Settings were saved, but the login-start entry is unavailable or owned by another file.");
        }

        PublishState(status: "Settings saved.");
        return new MacSettingsSaveResult(true, "Settings saved.");
    }

    public Task<bool> EnsureAccessibilityAndHotkeyAsync(bool prompt) =>
        TrackOperation(() => EnsureAccessibilityAndHotkeyCoreAsync(prompt));

    private async Task<bool> EnsureAccessibilityAndHotkeyCoreAsync(bool prompt)
    {
        if (!_selectionService.EnsurePermission(prompt))
        {
            PublishState(status: "Grant Accessibility permission in System Settings, then try again.");
            return false;
        }

        if (_hotkeyStarted)
        {
            return true;
        }

        var hotkeyRead = await _hotkeySettingsStore.ReadAsync(_lifetimeCancellation.Token);
        var settings = hotkeyRead.Succeeded ? hotkeyRead.Value! : _hotkeyService.Settings;
        var status = await _hotkeyService.StartAsync(settings, _lifetimeCancellation.Token);
        _hotkeyStarted = status == MacGlobalHotkeyStatus.Registered;
        PublishState(status: _hotkeyStarted
            ? "Accessibility permission and global hotkey are ready."
            : "The global hotkey could not be registered.");
        return _hotkeyStarted;
    }

    public Task<HistoryReadResult> LoadHistoryAsync(CancellationToken cancellationToken) =>
        TrackOperation(() => LoadHistoryCoreAsync(cancellationToken));

    private async Task<HistoryReadResult> LoadHistoryCoreAsync(CancellationToken cancellationToken)
    {
        var configuration = await _configurationStore.ReadAsync(cancellationToken);
        var retention = configuration.Succeeded
            ? configuration.Value!.HistoryRetention
            : DefaultRetention;
        return await _historyStore.ReadAsync(retention, cancellationToken);
    }

    public Task<PersistenceResult> ClearHistoryAsync(CancellationToken cancellationToken) =>
        TrackOperation(() => _historyStore.ClearAsync(cancellationToken));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _lifetimeCancellation.Cancel();
        CancelCurrentOperation();
        await WaitForTrackedOperationsAsync();
        _hotkeyService.Pressed -= HandleHotkeyPressed;
        try
        {
            await _hotkeyService.DisposeAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Independent stores must still be released after native hook cleanup fails.
        }

        DisposeNonFatal(_hotkeySettingsStore);
        DisposeNonFatal(_proxySettingsStore);
        DisposeNonFatal(_providerSettingsStore);
        DisposeNonFatal(_configurationStore);
        DisposeNonFatal(_credentialStore);
        DisposeNonFatal(_historyStore);
        DisposeNonFatal(_diagnosticSink);
        DisposeNonFatal(_httpClientPool);
        DisposeNonFatal(_lifetimeCancellation);
    }

    private async Task TranslateAsync(string text, QueryKind queryKind)
    {
        var (generation, cancellationToken) = BeginOperation();
        await TranslateAsync(text, queryKind, generation, cancellationToken);
    }

    private async Task TranslateAsync(
        string text,
        QueryKind queryKind,
        long generation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            PublishCurrentState(generation, input: text, status: "Enter text to translate.", isBusy: false);
            return;
        }

        PublishCurrentState(
            generation,
            input: text,
            output: string.Empty,
            status: "Loading provider settings...",
            isBusy: true,
            canRetry: false);
        var settings = await LoadTranslationSettingsAsync(cancellationToken);
        if (!IsCurrent(generation))
        {
            settings.Credential?.Dispose();
            return;
        }

        var credential = settings.Credential;
        if (!settings.Succeeded)
        {
            credential?.Dispose();
            PublishCurrentState(generation, status: settings.Error!, isBusy: false);
            return;
        }

        var profile = settings.Profile!;
        var configuration = settings.Configuration!;
        if (!_providers.TryResolve(profile.Provider, out var provider) || provider is null)
        {
            credential?.Dispose();
            PublishCurrentState(generation, status: "The selected provider is unavailable.", isBusy: false);
            return;
        }

        string? storedCredential;
        using (credential)
        {
            storedCredential = credential?.Reveal();
        }

        // Provider requests retain only the minimum transient strings after Keychain-backed bytes are zeroed.
        TranslationCredentials credentials;
        if (string.Equals(
                profile.Provider.ProviderId,
                TranslationProviderIds.Volcengine,
                StringComparison.Ordinal))
        {
            if (!VolcengineCredentialCodec.TryDecode(storedCredential, out credentials))
            {
                PublishCurrentState(
                    generation,
                    status: "The saved Volcengine credential is invalid.",
                    isBusy: false);
                return;
            }
        }
        else
        {
            credentials = new TranslationCredentials(storedCredential);
        }
        var requestId = Guid.NewGuid().ToString("N");
        var request = new TranslationProviderRequest(
            profile.Provider,
            profile.Endpoint,
            profile.Model,
            text,
            profile.SourceLanguage,
            profile.TargetLanguage,
            credentials,
            TimeSpan.FromSeconds(profile.TimeoutSeconds));
        var output = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        TranslationStreamEventKind terminalKind = TranslationStreamEventKind.Failed;
        QueryErrorCode? terminalError = QueryErrorCode.Internal;
        var retryable = false;
        var terminalReceived = false;
        try
        {
            PublishCurrentState(generation, status: "Receiving translation...", isBusy: true);
            await WriteDiagnosticAsync(
                DiagnosticEventId.TranslationStarted,
                DiagnosticOutcome.Succeeded,
                profile.Provider.ProviderId,
                requestId,
                null,
                null);
            await foreach (var streamEvent in provider.TranslateAsync(request, cancellationToken)
                               .WithCancellation(cancellationToken))
            {
                if (!IsCurrent(generation))
                {
                    return;
                }

                streamEvent.Validate();
                switch (streamEvent.Kind)
                {
                    case TranslationStreamEventKind.Delta:
                        output.Append(streamEvent.Text);
                        PublishCurrentState(generation, output: output.ToString());
                        break;
                    case TranslationStreamEventKind.Completed:
                        if (output.Length == 0)
                        {
                            terminalKind = TranslationStreamEventKind.Failed;
                            terminalError = QueryErrorCode.Internal;
                            PublishCurrentState(
                                generation,
                                status: DescribeQueryError(terminalError.Value),
                                isBusy: false,
                                canRetry: false);
                        }
                        else
                        {
                            terminalKind = streamEvent.Kind;
                            terminalError = null;
                            PublishCurrentState(
                                generation,
                                output: output.ToString(),
                                status: "Translation completed.",
                                isBusy: false,
                                canRetry: false);
                        }
                        break;
                    case TranslationStreamEventKind.Cancelled:
                        terminalKind = streamEvent.Kind;
                        terminalError = null;
                        PublishCurrentState(generation, status: "Translation cancelled.", isBusy: false);
                        break;
                    case TranslationStreamEventKind.Failed:
                        terminalKind = streamEvent.Kind;
                        terminalError = streamEvent.ErrorCode ?? QueryErrorCode.Internal;
                        retryable = streamEvent.Retryable;
                        PublishCurrentState(
                            generation,
                            status: DescribeQueryError(terminalError.Value),
                            isBusy: false,
                            canRetry: retryable);
                        break;
                }

                if (streamEvent.IsTerminal)
                {
                    terminalReceived = true;
                    break;
                }
            }

            if (!terminalReceived && IsCurrent(generation))
            {
                terminalError = QueryErrorCode.ProviderUnavailable;
                retryable = true;
                PublishCurrentState(
                    generation,
                    status: DescribeQueryError(terminalError.Value),
                    isBusy: false,
                    canRetry: true);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            terminalKind = TranslationStreamEventKind.Cancelled;
            terminalError = null;
            PublishCurrentState(generation, status: "Translation cancelled.", isBusy: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            terminalKind = TranslationStreamEventKind.Failed;
            terminalError = QueryErrorCode.Internal;
            PublishCurrentState(generation, status: DescribeQueryError(QueryErrorCode.Internal), isBusy: false);
        }
        finally
        {
            stopwatch.Stop();
        }

        if (!IsCurrent(generation))
        {
            return;
        }

        lock (_stateGate)
        {
            _retry = retryable ? new RetrySnapshot(text, queryKind) : null;
        }

        await AppendHistoryAsync(
            requestId,
            text,
            queryKind,
            profile,
            configuration,
            terminalKind,
            output.ToString(),
            terminalError,
            retryable);
        await WriteDiagnosticAsync(
            terminalKind switch
            {
                TranslationStreamEventKind.Completed => DiagnosticEventId.TranslationCompleted,
                TranslationStreamEventKind.Cancelled => DiagnosticEventId.TranslationCancelled,
                _ => DiagnosticEventId.TranslationFailed,
            },
            terminalKind switch
            {
                TranslationStreamEventKind.Completed => DiagnosticOutcome.Succeeded,
                TranslationStreamEventKind.Cancelled => DiagnosticOutcome.Cancelled,
                _ => DiagnosticOutcome.Failed,
            },
            profile.Provider.ProviderId,
            requestId,
            ToDiagnosticError(terminalError),
            stopwatch.ElapsedMilliseconds);
    }

    private async Task<TranslationSettingsResult> LoadTranslationSettingsAsync(
        CancellationToken cancellationToken)
    {
        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken);
        if (!providerRead.Succeeded)
        {
            return TranslationSettingsResult.Failed("Open Settings and configure a translation provider.");
        }

        var configurationRead = await _configurationStore.ReadAsync(cancellationToken);
        if (!configurationRead.Succeeded)
        {
            return TranslationSettingsResult.Failed("General provider settings are unavailable.");
        }

        var configuration = configurationRead.Value!;
        var key = configuration.DefaultProvider.InstanceId is null
            ? configuration.DefaultProvider.ProviderId
            : configuration.DefaultProvider.ProviderId + ":" + configuration.DefaultProvider.InstanceId;
        var profile = providerRead.Value!.Profiles.FirstOrDefault(candidate =>
            string.Equals(candidate.CanonicalProviderKey, key, StringComparison.Ordinal));
        if (profile is null)
        {
            return TranslationSettingsResult.Failed("The selected provider profile is unavailable.");
        }

        var definition = FindProviderDefinition(profile.Provider.ProviderId);
        if (definition is null)
        {
            return TranslationSettingsResult.Failed("The selected provider is unsupported.");
        }

        if (definition.CredentialKind == ProviderCredentialKind.None)
        {
            return TranslationSettingsResult.Success(profile, configuration, null);
        }

        var credentialRead = await _credentialStore.GetAsync(
            new CredentialKey(profile.Provider.ProviderId, profile.Provider.InstanceId),
            cancellationToken);
        if (credentialRead.Succeeded)
        {
            return TranslationSettingsResult.Success(profile, configuration, credentialRead.Value);
        }

        return definition.CredentialKind == ProviderCredentialKind.Optional &&
            credentialRead.Status == PersistenceStatus.NotFound
            ? TranslationSettingsResult.Success(profile, configuration, null)
            : TranslationSettingsResult.Failed("The selected provider credential is unavailable.");
    }

    private async Task<MacSettingsSaveResult?> SaveCredentialAsync(
        MacSettingsInput input,
        CancellationToken cancellationToken)
    {
        var definition = FindProviderDefinition(input.ProviderId)!;
        var key = new CredentialKey(input.ProviderId);
        if (definition.CredentialKind == ProviderCredentialKind.None || input.ClearCredential)
        {
            var remove = await _credentialStore.RemoveAsync(key, cancellationToken);
            return remove.Status is PersistenceStatus.Succeeded or PersistenceStatus.NotFound
                ? null
                : new MacSettingsSaveResult(false, "The provider credential could not be cleared.");
        }

        string? value = null;
        if (definition.CredentialKind == ProviderCredentialKind.VolcenginePair)
        {
            var hasPrimary = !string.IsNullOrWhiteSpace(input.Credential);
            var hasSecondary = !string.IsNullOrWhiteSpace(input.SecondaryCredential);
            if (hasPrimary != hasSecondary)
            {
                return new MacSettingsSaveResult(false, "Both Volcengine AK and SK are required together.");
            }

            if (hasPrimary)
            {
                value = VolcengineCredentialCodec.Encode(input.Credential!, input.SecondaryCredential!);
            }
        }
        else if (!string.IsNullOrEmpty(input.Credential))
        {
            value = input.Credential;
        }

        if (value is null)
        {
            return null;
        }

        using var secret = new CredentialSecret(value);
        var set = await _credentialStore.SetAsync(key, secret, cancellationToken);
        return set.Succeeded
            ? null
            : new MacSettingsSaveResult(false, "The provider credential could not be saved to Keychain.");
    }

    private static bool TryValidateSettingsInput(
        MacSettingsInput? input,
        out ProviderProfileSettings? profile,
        out string? error)
    {
        profile = null;
        error = null;
        if (input is null || FindProviderDefinition(input.ProviderId) is null)
        {
            error = "Choose a supported provider.";
            return false;
        }

        if (!Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var endpoint))
        {
            error = "Enter an absolute HTTP(S) provider endpoint.";
            return false;
        }

        try
        {
            profile = new ProviderProfileSettings(
                new ProviderDescriptor(input.ProviderId),
                endpoint,
                string.IsNullOrWhiteSpace(input.Model) ? null : input.Model.Trim(),
                string.IsNullOrWhiteSpace(input.SourceLanguage) ? null : input.SourceLanguage.Trim(),
                input.TargetLanguage.Trim(),
                input.TimeoutSeconds);
            profile.Validate();
            input.ProxySettings.Validate();
            input.HotkeySettings.Validate();
            input.HistoryRetention.Validate();
            if (FindProviderDefinition(input.ProviderId)!.ModelRequired && profile.Model is null)
            {
                error = "The selected provider requires a model.";
                profile = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            profile = null;
            error = "One or more settings values are invalid.";
            return false;
        }
    }

    private async Task AppendHistoryAsync(
        string requestId,
        string input,
        QueryKind queryKind,
        ProviderProfileSettings profile,
        Configuration configuration,
        TranslationStreamEventKind terminalKind,
        string output,
        QueryErrorCode? errorCode,
        bool retryable)
    {
        try
        {
            var request = new QueryRequest(
                1,
                requestId,
                queryKind,
                input,
                profile.SourceLanguage,
                profile.TargetLanguage,
                profile.Provider);
            var result = terminalKind switch
            {
                TranslationStreamEventKind.Completed when !string.IsNullOrWhiteSpace(output) => new QueryResult(
                    1,
                    requestId,
                    queryKind,
                    profile.Provider,
                    QueryTerminalState.Completed,
                    profile.SourceLanguage,
                    profile.TargetLanguage,
                    new QueryResultPayload(output)),
                TranslationStreamEventKind.Cancelled => new QueryResult(
                    1,
                    requestId,
                    queryKind,
                    profile.Provider,
                    QueryTerminalState.Cancelled,
                    profile.SourceLanguage,
                    profile.TargetLanguage),
                _ => new QueryResult(
                    1,
                    requestId,
                    queryKind,
                    profile.Provider,
                    QueryTerminalState.Failed,
                    profile.SourceLanguage,
                    profile.TargetLanguage,
                    Error: new QueryError(
                        errorCode ?? QueryErrorCode.Internal,
                        DescribeQueryError(errorCode ?? QueryErrorCode.Internal),
                        retryable)),
            };
            await _historyStore.AppendAsync(
                new HistoryEntry(1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, request, result),
                configuration.HistoryRetention,
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // History persistence cannot change a completed translation outcome.
        }
    }

    private async Task WriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticOutcome outcome,
        string? providerId,
        string? requestId,
        DiagnosticErrorCode? errorCode,
        long? durationMs)
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
                    durationMs),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Diagnostics cannot change the primary operation outcome.
        }
    }

    private (long Generation, CancellationToken Token) BeginOperation()
    {
        lock (_operationGate)
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();
            return (++_operationGeneration, _operationCancellation.Token);
        }
    }

    private bool IsCurrent(long generation)
    {
        lock (_operationGate)
        {
            return Volatile.Read(ref _disposeRequested) == 0 && generation == _operationGeneration;
        }
    }

    private void PublishCurrentState(
        long generation,
        string? input = null,
        string? output = null,
        string? status = null,
        bool? isBusy = null,
        bool? canRetry = null)
    {
        if (IsCurrent(generation))
        {
            PublishState(input, output, status, isBusy, canRetry);
        }
    }

    private void PublishState(
        string? input = null,
        string? output = null,
        string? status = null,
        bool? isBusy = null,
        bool? canRetry = null)
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        MacRuntimeState state;
        lock (_stateGate)
        {
            _state = _state with
            {
                Input = input ?? _state.Input,
                Output = output ?? _state.Output,
                Status = status ?? _state.Status,
                IsBusy = isBusy ?? _state.IsBusy,
                CanRetry = canRetry ?? _state.CanRetry,
            };
            state = _state;
        }

        StateChanged?.Invoke(this, state);
    }

    private void HandleHotkeyPressed(object? sender, EventArgs eventArgs) =>
        _ = TranslateSelectedTextAsync(promptForPermission: false);

    private Task TrackOperation(Func<Task> operationFactory)
    {
        Task operation;
        lock (_trackedOperationsGate)
        {
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return Task.CompletedTask;
            }

            operation = operationFactory();
            _trackedOperations.Add(operation);
        }

        AttachTrackedOperationContinuation(operation);
        return operation;
    }

    private Task<T> TrackOperation<T>(Func<Task<T>> operationFactory)
    {
        Task<T> operation;
        lock (_trackedOperationsGate)
        {
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return Task.FromException<T>(new ObjectDisposedException(nameof(MacAppRuntime)));
            }

            operation = operationFactory();
            _trackedOperations.Add(operation);
        }

        AttachTrackedOperationContinuation(operation);
        return operation;
    }

    private void AttachTrackedOperationContinuation(Task operation) => operation.ContinueWith(
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
                await Task.WhenAll(operations).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // Each operation owns its visible failure state; shutdown still waits for all continuations.
            }
        }
    }

    private static void DisposeNonFatal(IDisposable disposable)
    {
        try
        {
            disposable.Dispose();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Shutdown continues so independent managed and native resources can still be released.
        }
    }

    private static ProviderDefinition? FindProviderDefinition(string providerId) =>
        ProviderDefinitions.FirstOrDefault(definition =>
            string.Equals(definition.ProviderId, providerId, StringComparison.Ordinal));

    private static Configuration DefaultConfiguration() => new(
        1,
        ConfigurationMigration.CurrentVersion,
        new ProviderDescriptor(TranslationProviderIds.OpenAiCompatible),
        DefaultRetention);

    private static string DescribeSelectionFailure(MacSelectionStatus status) => status switch
    {
        MacSelectionStatus.PermissionRequired =>
            "Accessibility permission is required. Grant it in System Settings and try again.",
        MacSelectionStatus.NoFocusedElement => "No focused application exposed selected text.",
        MacSelectionStatus.NoSelection => "No selected text was found.",
        MacSelectionStatus.Unsupported => "The focused application does not expose its selected text.",
        _ => "Selected text could not be read.",
    };

    private static string DescribeOcrFailure(MacOcrStatus status) => status switch
    {
        MacOcrStatus.NoText => "No text was recognized in the selected region.",
        MacOcrStatus.LanguageUnavailable => "The selected OCR language is unavailable.",
        MacOcrStatus.Cancelled => "OCR cancelled.",
        MacOcrStatus.Unsupported => "macOS Vision OCR is unavailable on this system.",
        _ => "OCR failed.",
    };

    private static string DescribeQueryError(QueryErrorCode errorCode) => errorCode switch
    {
        QueryErrorCode.InvalidRequest => "The translation request is invalid.",
        QueryErrorCode.ProviderUnavailable => "The translation provider is unavailable.",
        QueryErrorCode.Timeout => "The translation request timed out.",
        QueryErrorCode.Network => "The translation provider could not be reached.",
        QueryErrorCode.Authentication => "The translation credential was rejected.",
        QueryErrorCode.RateLimited => "The translation provider rate limit was reached.",
        QueryErrorCode.UnsupportedLanguage => "The requested language is unsupported.",
        _ => "The translation failed.",
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

    private sealed record TranslationSettingsResult(
        ProviderProfileSettings? Profile,
        Configuration? Configuration,
        CredentialSecret? Credential,
        string? Error)
    {
        public bool Succeeded => Profile is not null && Configuration is not null && Error is null;

        public static TranslationSettingsResult Success(
            ProviderProfileSettings profile,
            Configuration configuration,
            CredentialSecret? credential) => new(profile, configuration, credential, null);

        public static TranslationSettingsResult Failed(string error) => new(null, null, null, error);
    }
}
