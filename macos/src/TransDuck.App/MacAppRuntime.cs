using System.Diagnostics;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Persistence;
using TransDuck.Infrastructure.Lookup;
using TransDuck.Infrastructure.Proxy;
using TransDuck.Infrastructure.Translation;
using TransDuck.Platform.MacOS.Capture;
using TransDuck.Platform.MacOS.Dictionary;
using TransDuck.Platform.MacOS.Hotkeys;
using TransDuck.Platform.MacOS.Ocr;
using TransDuck.Platform.MacOS.Persistence;
using TransDuck.Platform.MacOS.Selection;
using TransDuck.Platform.MacOS.Speech;
using TransDuck.Platform.MacOS.Startup;
using TransDuck.UI;

namespace TransDuck.MacOS.App;

internal sealed class MacAppRuntime : IAsyncDisposable
{
    private static readonly HistoryRetention DefaultRetention = new(100, 30);
    private readonly MacDataPaths _dataPaths = new();
    private readonly JsonConfigurationStore _configurationStore;
    private readonly JsonProviderSettingsStore _providerSettingsStore;
    private readonly JsonQuerySourceSettingsStore _querySourceSettingsStore;
    private readonly JsonProxySettingsStore _proxySettingsStore;
    private readonly JsonMacHotkeySettingsStore _hotkeySettingsStore;
    private readonly JsonMacHotkeySettingsStore _inputHotkeySettingsStore;
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
    private readonly LocalDictionaryProvider _localDictionaryProvider;
    private readonly MacSystemDictionaryProvider _systemDictionaryProvider = new MacSystemDictionaryProvider();
    private readonly ISystemSpeechPlayer _speechPlayer = new MacSystemSpeechPlayer();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _stateGate = new();
    private readonly object _operationGate = new();
    private readonly object _trackedOperationsGate = new();
    private readonly HashSet<Task> _trackedOperations = [];
    private MacRuntimeState _state = new(
        string.Empty,
        string.Empty,
        UiStrings.Get("mac.status.starting"),
        false,
        false,
        [],
        Revision: 0);
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
        _querySourceSettingsStore = new JsonQuerySourceSettingsStore(_dataPaths);
        _proxySettingsStore = new JsonProxySettingsStore(_dataPaths);
        _hotkeySettingsStore = new JsonMacHotkeySettingsStore(_dataPaths);
        _inputHotkeySettingsStore = new JsonMacHotkeySettingsStore(_dataPaths.InputHotkeySettingsFilePath);
        _historyStore = new JsonLinesHistoryStore(_dataPaths);
        _diagnosticSink = new JsonLinesDiagnosticSink(_dataPaths);
        _localDictionaryProvider = new LocalDictionaryProvider(
            Path.Combine(_dataPaths.RootDirectory, "dictionary-cache"));
        var leaseSource = new ProxyTranslationHttpClientLeaseSource(_httpClientPool);
        _providers.Register(new OpenAiCompatibleProvider(leaseSource));
        _providers.Register(new DeepLProvider(leaseSource));
        _providers.Register(new OllamaProvider(leaseSource));
        _providers.Register(new BingWebProvider(leaseSource));
        _providers.Register(new GoogleWebProvider(leaseSource));
        _providers.Register(new VolcengineProvider(leaseSource));
        _hotkeyService.Pressed += HandleHotkeyPressed;
        _hotkeyService.InputRequested += HandleInputHotkeyPressed;
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
            UiStrings.Get("provider.name.bing"),
            BingWebProvider.DefaultEndpoint,
            ModelRequired: false,
            ProviderCredentialKind.Optional),
        new(
            TranslationProviderIds.Google,
            UiStrings.Get("provider.name.google"),
            GoogleWebProvider.DefaultEndpoint,
            ModelRequired: false,
            ProviderCredentialKind.None),
        new(
            TranslationProviderIds.Volcengine,
            UiStrings.Get("provider.name.volcengine"),
            VolcengineProvider.DefaultEndpoint,
            ModelRequired: false,
            ProviderCredentialKind.VolcenginePair),
    ];

    public event EventHandler<MacRuntimeState>? StateChanged;

    public event EventHandler? PresentationRequested;
    public event EventHandler? InputPresentationRequested;

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
        status: UiStrings.Get("mac.status.startup_failed"),
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
                status.Add(UiStrings.Get("mac.status.proxy_loaded"));
            }
            catch (ArgumentException)
            {
                status.Add(UiStrings.Get("mac.status.proxy_invalid"));
            }
        }
        else if (proxyRead.Status != PersistenceStatus.NotFound)
        {
            status.Add(UiStrings.Get("mac.status.proxy_unavailable"));
        }

        var hotkeyRead = await _hotkeySettingsStore.ReadAsync(cancellationToken);
        var hotkey = hotkeyRead.Succeeded ? hotkeyRead.Value! : MacHotkeySettings.Default;
        _hotkeyService.TrySetSettings(hotkey);
        var inputRead = await _inputHotkeySettingsStore.ReadAsync(cancellationToken);
        var inputHotkey = inputRead.Succeeded ? inputRead.Value! : MacHotkeySettings.InputDefault;
        if (!_hotkeyService.TrySetInputSettings(inputHotkey))
        {
            status.Add(UiStrings.Get("hotkey.input.conflict"));
        }
        if (_selectionService.EnsurePermission(prompt: false))
        {
            var hotkeyStatus = await _hotkeyService.StartAsync(hotkey, cancellationToken);
            _hotkeyStarted = hotkeyStatus == MacGlobalHotkeyStatus.Registered;
            status.Add(_hotkeyStarted ? UiStrings.Get("mac.status.hotkey_ready") : UiStrings.Get("mac.status.hotkey_unavailable"));
        }
        else
        {
            _hotkeyService.TrySetSettings(hotkey);
            status.Add(UiStrings.Get("mac.status.accessibility_needed"));
        }

        var configuration = await _configurationStore.ReadAsync(cancellationToken);
        var profiles = await _providerSettingsStore.ReadAsync(cancellationToken);
        var querySources = await _querySourceSettingsStore.ReadAsync(cancellationToken);
        var effectiveSources = querySources.Succeeded
            ? querySources.Value!
            : configuration.Succeeded
                ? QuerySourceSettings.CreateDefault(configuration.Value!.DefaultProvider)
                : null;
        var configuredProviderKeys = profiles.Succeeded
            ? profiles.Value!.Profiles.Select(static profile => profile.CanonicalProviderKey)
                .ToHashSet(StringComparer.Ordinal)
            : [];
        var hasUsableSource = effectiveSources is not null &&
            (effectiveSources.LocalDictionary.Enabled || effectiveSources.MacSystemDictionaryEnabled ||
             effectiveSources.EnabledTranslationProviders.Any(provider =>
                 configuredProviderKeys.Contains(CanonicalProviderKey(provider))));
        if (!hasUsableSource)
        {
            status.Add(UiStrings.Get("mac.status.configure_sources"));
        }

        PublishState(status: string.Join("; ", status) + ".");
    }

    public Task TranslateAsync(string text) =>
        TrackOperation(() => TranslateAsync(text, QueryKind.Translation));

    public Task TranslateSelectedTextAsync(bool promptForPermission) =>
        TrackOperation(() => TranslateSelectedTextCoreAsync(promptForPermission));

    public Task PronounceAsync(string text) =>
        TrackOperation(() => PronounceCoreAsync(text));

    public void StopPronunciation() => _speechPlayer.Stop();

    private async Task PronounceCoreAsync(string text)
    {
        var result = await _speechPlayer.SpeakAsync(text, _lifetimeCancellation.Token);
        if (result.Status is SpeechPlaybackStatus.Completed or SpeechPlaybackStatus.Cancelled)
        {
            return;
        }

        PublishState(status: result.Status == SpeechPlaybackStatus.Unavailable
            ? UiStrings.Get("pronunciation.status.unavailable")
            : UiStrings.Get("pronunciation.status.failed"));
    }

    private async Task TranslateSelectedTextCoreAsync(bool promptForPermission)
    {
        if (promptForPermission && !_hotkeyStarted)
        {
            await EnsureAccessibilityAndHotkeyAsync(prompt: true);
        }

        var selection = _selectionService.ReadSelectedText(promptForPermission: false);
        PresentationRequested?.Invoke(this, EventArgs.Empty);
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
        PublishCurrentState(generation, status: UiStrings.Get("mac.status.select_region"), isBusy: true);
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
                    ? UiStrings.Get("mac.status.capture_cancelled")
                    : capture.Status == MacScreenCaptureStatus.PermissionRequired
                        ? UiStrings.Get("mac.status.screen_permission")
                    : UiStrings.Get("mac.status.capture_failed"),
                isBusy: false);
            return;
        }

        PublishCurrentState(generation, status: UiStrings.Get("mac.status.recognizing"), isBusy: true);
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

        MarkActiveSourcesCancelled();
    }

    public Task RetryAsync()
    {
        RetrySnapshot? retry;
        lock (_stateGate)
        {
            retry = _retry;
        }

        return retry is null
            ? Task.CompletedTask
            : TranslateAsync(retry.Text, retry.QueryKind, retry.SourceKeys);
    }

    public Task<MacSettingsSnapshot> LoadSettingsAsync(CancellationToken cancellationToken) =>
        TrackOperation(() => LoadSettingsCoreAsync(cancellationToken));

    private async Task<MacSettingsSnapshot> LoadSettingsCoreAsync(CancellationToken cancellationToken)
    {
        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken);
        var configurationRead = await _configurationStore.ReadAsync(cancellationToken);
        var querySourceRead = await _querySourceSettingsStore.ReadAsync(cancellationToken);
        var proxyRead = await _proxySettingsStore.ReadAsync(cancellationToken);
        var inputRead = await _inputHotkeySettingsStore.ReadAsync(cancellationToken);
        var hotkeyRead = await _hotkeySettingsStore.ReadAsync(cancellationToken);
        var configuration = configurationRead.Succeeded
            ? configurationRead.Value!
            : DefaultConfiguration();
        var querySources = querySourceRead.Succeeded
            ? querySourceRead.Value!
            : QuerySourceSettings.CreateDefault(configuration.DefaultProvider);
        return new MacSettingsSnapshot(
            configuration,
            providerRead.Succeeded ? providerRead.Value!.Profiles : [],
            proxyRead.Succeeded ? proxyRead.Value! : _httpClientPool.CurrentSettings,
            hotkeyRead.Succeeded ? hotkeyRead.Value! : _hotkeyService.Settings,
            _startupService.GetStatus(),
            querySources,
            providerRead.Status,
            configurationRead.Status,
            querySourceRead.Status,
            proxyRead.Status,
            hotkeyRead.Status,
            inputRead.Succeeded ? inputRead.Value! : _hotkeyService.InputSettings ?? MacHotkeySettings.InputDefault);
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

    public Task<MacSettingsSaveResult> SaveQuerySourcesAsync(
        QuerySourceSettings settings,
        CancellationToken cancellationToken) =>
        TrackOperation(() => SaveQuerySourcesCoreAsync(settings, cancellationToken));

    private async Task<MacSettingsSaveResult> SaveQuerySourcesCoreAsync(
        QuerySourceSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            settings.Validate();
        }
        catch (ContractValidationException)
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("mac.settings.invalid_sources"));
        }

        var write = await _querySourceSettingsStore.WriteAsync(settings, cancellationToken);
        return write.Succeeded
            ? new MacSettingsSaveResult(true, UiStrings.Get("settings.status.sources_saved"))
            : new MacSettingsSaveResult(false, UiStrings.Get("mac.settings.sources_save_failed"));
    }

    public Task<MacSettingsSaveResult> SaveInputHotkeyAsync(
        MacHotkeySettings settings, CancellationToken cancellationToken) =>
        TrackOperation(async () =>
        {
            try { settings.Validate(); }
            catch (ContractValidationException)
            {
                return new MacSettingsSaveResult(false, UiStrings.Get("hotkey.ui.invalid"));
            }
            if (settings.UsesSameChord(_hotkeyService.Settings))
                return new MacSettingsSaveResult(false, UiStrings.Get("hotkey.input.conflict"));
            var write = await _inputHotkeySettingsStore.WriteAsync(settings, cancellationToken);
            if (!write.Succeeded || !_hotkeyService.TrySetInputSettings(settings))
                return new MacSettingsSaveResult(false, UiStrings.Get("hotkey.input.save_failed"));
            return new MacSettingsSaveResult(true, UiStrings.Get(_hotkeyStarted
                ? "hotkey.input.saved" : "hotkey.input.saved_inactive"));
        });

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

        if (_hotkeyService.InputSettings is { } inputHotkey && input.HotkeySettings.UsesSameChord(inputHotkey))
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("hotkey.input.conflict"));
        }
        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken);
        if (providerRead.Status is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound))
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("provider.status.settings_unavailable"));
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
            return new MacSettingsSaveResult(false, UiStrings.Get("provider.save.write_failed"));
        }

        var configuration = new Configuration(
            1,
            ConfigurationMigration.CurrentVersion,
            profile!.Provider,
            input.HistoryRetention);
        var configurationWrite = await _configurationStore.WriteAsync(configuration, cancellationToken);
        if (!configurationWrite.Succeeded)
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("mac.settings.general_save_failed"));
        }

        var querySourceWrite = await _querySourceSettingsStore.WriteAsync(
            input.QuerySourceSettings,
            cancellationToken);
        if (!querySourceWrite.Succeeded)
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("mac.settings.sources_save_failed"));
        }

        var credentialResult = await SaveCredentialAsync(input, cancellationToken);
        if (credentialResult is not null)
        {
            return credentialResult;
        }

        var proxyWrite = await _proxySettingsStore.WriteAsync(input.ProxySettings, cancellationToken);
        if (!proxyWrite.Succeeded)
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("mac.settings.proxy_save_failed"));
        }

        try
        {
            _httpClientPool.Update(input.ProxySettings);
        }
        catch (ArgumentException)
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("mac.settings.proxy_apply_failed"));
        }

        var hotkeyWrite = await _hotkeySettingsStore.WriteAsync(input.HotkeySettings, cancellationToken);
        if (!hotkeyWrite.Succeeded || !_hotkeyService.TrySetSettings(input.HotkeySettings))
        {
            return new MacSettingsSaveResult(false, UiStrings.Get("mac.settings.hotkey_save_failed"));
        }

        if (_hotkeyService.InputSettings is null)
        {
            var inputRead = await _inputHotkeySettingsStore.ReadAsync(cancellationToken);
            _hotkeyService.TrySetInputSettings(inputRead.Succeeded ? inputRead.Value! : MacHotkeySettings.InputDefault);
        }

        var startup = input.StartAtLogin
            ? await _startupService.EnableAsync(cancellationToken)
            : await _startupService.DisableAsync(cancellationToken);
        if (startup.Status is MacStartupStatus.Conflict or MacStartupStatus.Failed)
        {
            return new MacSettingsSaveResult(
                false,
                UiStrings.Get("mac.settings.startup_failed"));
        }

        PublishState(status: UiStrings.Get("mac.settings.saved"));
        return new MacSettingsSaveResult(true, UiStrings.Get("mac.settings.saved"));
    }

    public Task<bool> EnsureAccessibilityAndHotkeyAsync(bool prompt) =>
        TrackOperation(() => EnsureAccessibilityAndHotkeyCoreAsync(prompt));

    private async Task<bool> EnsureAccessibilityAndHotkeyCoreAsync(bool prompt)
    {
        if (!_selectionService.EnsurePermission(prompt))
        {
            PublishState(status: prompt
                ? UiStrings.Get("mac.status.accessibility_pending")
                : UiStrings.Get("mac.status.accessibility_required"));
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
            ? UiStrings.Get("mac.status.accessibility_ready")
            : UiStrings.Get("mac.status.hotkey_failed"));
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
        _speechPlayer.Stop();
        CancelCurrentOperation();
        await WaitForTrackedOperationsAsync();
        _hotkeyService.Pressed -= HandleHotkeyPressed;
        _hotkeyService.InputRequested -= HandleInputHotkeyPressed;
        try
        {
            await _hotkeyService.DisposeAsync();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Independent stores must still be released after native hook cleanup fails.
        }

        DisposeNonFatal(_hotkeySettingsStore);
        DisposeNonFatal(_inputHotkeySettingsStore);
        DisposeNonFatal(_proxySettingsStore);
        DisposeNonFatal(_providerSettingsStore);
        DisposeNonFatal(_querySourceSettingsStore);
        DisposeNonFatal(_configurationStore);
        DisposeNonFatal(_credentialStore);
        DisposeNonFatal(_historyStore);
        DisposeNonFatal(_diagnosticSink);
        DisposeNonFatal(_speechPlayer);
        DisposeNonFatal(_httpClientPool);
        DisposeNonFatal(_lifetimeCancellation);
    }

    private async Task TranslateAsync(
        string text,
        QueryKind queryKind,
        IReadOnlySet<string>? sourceFilter = null)
    {
        var (generation, cancellationToken) = BeginOperation();
        await TranslateAsync(text, queryKind, generation, cancellationToken, sourceFilter);
    }

    private async Task TranslateAsync(
        string text,
        QueryKind queryKind,
        long generation,
        CancellationToken cancellationToken,
        IReadOnlySet<string>? sourceFilter = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            PublishCurrentState(generation, input: text, status: UiStrings.Get("translation.input.empty"), isBusy: false);
            return;
        }

        PublishCurrentState(
            generation,
            input: text,
            output: sourceFilter is null ? string.Empty : null,
            status: UiStrings.Get("mac.status.loading_sources"),
            isBusy: true,
            canRetry: false,
            results: sourceFilter is null ? [] : null);
        var configurationRead = await _configurationStore.ReadAsync(cancellationToken);
        var configuration = configurationRead.Succeeded
            ? configurationRead.Value!
            : DefaultConfiguration();
        var providerRead = await _providerSettingsStore.ReadAsync(cancellationToken);
        var querySourceRead = await _querySourceSettingsStore.ReadAsync(cancellationToken);
        if (!IsCurrent(generation))
        {
            return;
        }

        if (querySourceRead.Status is not (PersistenceStatus.Succeeded or PersistenceStatus.NotFound))
        {
            PublishCurrentState(
                generation,
                status: UiStrings.Get("mac.status.sources_unavailable"),
                isBusy: false);
            return;
        }

        var sourceSettings = querySourceRead.Succeeded
            ? querySourceRead.Value!
            : QuerySourceSettings.CreateDefault(configuration.DefaultProvider);
        var providerSources = sourceSettings.EnabledTranslationProviders
            .Where(provider => sourceFilter is null ||
                sourceFilter.Contains(CanonicalProviderKey(provider)))
            .ToArray();
        var includeLocalDictionary = sourceSettings.LocalDictionary.Enabled &&
            (sourceFilter is null || sourceFilter.Contains(LocalDictionaryIds.File));
        var includeMacSystem = sourceSettings.MacSystemDictionaryEnabled &&
            (sourceFilter is null || sourceFilter.Contains(LocalDictionaryIds.MacSystem));
        var presentations = providerSources
            .Select(provider => new TranslationResultViewModel(
                CanonicalProviderKey(provider),
                DescribeProvider(provider),
                string.Empty,
                UiStrings.Get("result.source.waiting"),
                targetLanguage: providerRead.Value?.Profiles.FirstOrDefault(profile =>
                    profile.CanonicalProviderKey == CanonicalProviderKey(provider))?.TargetLanguage))
            .ToList();
        if (includeLocalDictionary)
        {
            presentations.Add(new TranslationResultViewModel(
                LocalDictionaryIds.File,
                UiStrings.Get("result.source.local_dictionary"),
                string.Empty,
                UiStrings.Get("result.source.waiting")));
        }

        if (includeMacSystem)
        {
            presentations.Add(new TranslationResultViewModel(
                LocalDictionaryIds.MacSystem,
                UiStrings.Get("result.source.mac_system_dictionary"),
                string.Empty,
                UiStrings.Get("result.source.waiting")));
        }

        var preparedResults = sourceFilter is null
            ? presentations
            : PrepareRetryResults(presentations);
        PublishCurrentState(
            generation,
            output: CombineResults(preparedResults),
            status: UiStrings.Get("mac.status.receiving"),
            isBusy: true,
            results: preparedResults);
        var providerDocument = providerRead.Succeeded ? providerRead.Value : null;
        var runs = providerSources
            .Select(provider => RunTranslationSourceAsync(
                provider,
                providerDocument,
                configuration,
                text,
                queryKind,
                generation,
                cancellationToken))
            .ToList();
        if (includeLocalDictionary)
        {
            runs.Add(RunDictionarySourceAsync(
                _localDictionaryProvider,
                sourceSettings.LocalDictionary.DataFilePath,
                text,
                configuration,
                generation,
                cancellationToken));
        }

        if (includeMacSystem)
        {
            runs.Add(RunDictionarySourceAsync(
                _systemDictionaryProvider,
                dataFilePath: null,
                text,
                configuration,
                generation,
                cancellationToken));
        }

        var terminals = await Task.WhenAll(runs);
        if (!IsCurrent(generation))
        {
            return;
        }

        var retryableKeys = terminals
            .Where(static terminal => terminal.Retryable)
            .Select(static terminal => terminal.Key)
            .ToHashSet(StringComparer.Ordinal);
        lock (_stateGate)
        {
            _retry = retryableKeys.Count > 0
                ? new RetrySnapshot(text, queryKind, retryableKeys)
                : null;
        }
        PublishCurrentState(
            generation,
            status: terminals.Any(static terminal => terminal.Succeeded)
                ? string.Empty
                : UiStrings.Get("mac.status.no_results"),
            isBusy: false,
            canRetry: retryableKeys.Count > 0);
    }

    private async Task<MacSourceTerminal> RunTranslationSourceAsync(
        ProviderDescriptor selectedProvider,
        ProviderSettingsDocument? providerDocument,
        Configuration configuration,
        string text,
        QueryKind queryKind,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RunTranslationSourceCoreAsync(
                selectedProvider,
                providerDocument,
                configuration,
                text,
                queryKind,
                generation,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MacSourceTerminal.Cancelled(CanonicalProviderKey(selectedProvider));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            PublishCurrentSourceResult(
                generation,
                CanonicalProviderKey(selectedProvider),
                DescribeProvider(selectedProvider),
                DescribeQueryError(QueryErrorCode.Internal),
                UiStrings.Get("result.source.failed"));
            return MacSourceTerminal.Failed(
                CanonicalProviderKey(selectedProvider),
                QueryErrorCode.Internal,
                retryable: false);
        }
    }

    private async Task<MacSourceTerminal> RunTranslationSourceCoreAsync(
        ProviderDescriptor selectedProvider,
        ProviderSettingsDocument? providerDocument,
        Configuration configuration,
        string text,
        QueryKind queryKind,
        long generation,
        CancellationToken cancellationToken)
    {
        var key = CanonicalProviderKey(selectedProvider);
        var displayName = DescribeProvider(selectedProvider);
        var settings = await LoadTranslationSettingsAsync(
            selectedProvider,
            providerDocument,
            configuration,
            cancellationToken);
        var credential = settings.Credential;
        if (!settings.Succeeded)
        {
            credential?.Dispose();
            PublishCurrentSourceResult(
                generation,
                key,
                displayName,
                settings.Error!,
                UiStrings.Get("mac.source.not_configured"));
            return MacSourceTerminal.Failed(key, QueryErrorCode.InvalidRequest, retryable: false);
        }

        var profile = settings.Profile!;
        if (!_providers.TryResolve(profile.Provider, out var provider) || provider is null)
        {
            credential?.Dispose();
            PublishCurrentSourceResult(
                generation,
                key,
                displayName,
                UiStrings.Get("mac.provider.unavailable"),
                UiStrings.Get("result.source.failed"));
            return MacSourceTerminal.Failed(key, QueryErrorCode.ProviderUnavailable, retryable: false);
        }

        string? storedCredential;
        using (credential)
        {
            storedCredential = credential?.Reveal();
        }

        TranslationCredentials credentials;
        if (string.Equals(profile.Provider.ProviderId, TranslationProviderIds.Volcengine, StringComparison.Ordinal))
        {
            if (!VolcengineCredentialCodec.TryDecode(storedCredential, out credentials))
            {
                PublishCurrentSourceResult(
                    generation,
                    key,
                    displayName,
                    UiStrings.Get("mac.credential.invalid_pair"),
                    UiStrings.Get("result.source.failed"));
                return MacSourceTerminal.Failed(key, QueryErrorCode.Authentication, retryable: false);
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
        var stopwatch = Stopwatch.StartNew();
        await WriteDiagnosticAsync(
            DiagnosticEventId.TranslationStarted,
            DiagnosticOutcome.Succeeded,
            profile.Provider.ProviderId,
            requestId,
            null,
            null);
        PublishCurrentSourceResult(generation, key, displayName, string.Empty, UiStrings.Get("result.source.receiving"));
        var result = await TranslationProviderRunner.RunAsync(
            provider,
            request,
            output => PublishCurrentSourceResult(
                generation,
                key,
                displayName,
                output,
                UiStrings.Get("result.source.receiving")),
            cancellationToken);
        stopwatch.Stop();
        var output = string.IsNullOrWhiteSpace(result.Text) && result.ErrorCode is { } error
            ? DescribeQueryError(error)
            : result.Text;
        PublishCurrentSourceResult(
            generation,
            key,
            displayName,
            output,
            DescribeSourceTerminal(result.TerminalKind));
        await AppendHistoryAsync(
            requestId,
            text,
            queryKind,
            profile,
            configuration,
            result.TerminalKind,
            result.Text,
            result.ErrorCode,
            result.Retryable);
        await WriteDiagnosticAsync(
            result.TerminalKind switch
            {
                TranslationStreamEventKind.Completed => DiagnosticEventId.TranslationCompleted,
                TranslationStreamEventKind.Cancelled => DiagnosticEventId.TranslationCancelled,
                _ => DiagnosticEventId.TranslationFailed,
            },
            result.TerminalKind switch
            {
                TranslationStreamEventKind.Completed => DiagnosticOutcome.Succeeded,
                TranslationStreamEventKind.Cancelled => DiagnosticOutcome.Cancelled,
                _ => DiagnosticOutcome.Failed,
            },
            profile.Provider.ProviderId,
            requestId,
            ToDiagnosticError(result.ErrorCode),
            stopwatch.ElapsedMilliseconds);
        return result.TerminalKind == TranslationStreamEventKind.Completed
            ? MacSourceTerminal.Completed(key)
            : result.TerminalKind == TranslationStreamEventKind.Cancelled
                ? MacSourceTerminal.Cancelled(key)
                : MacSourceTerminal.Failed(
                    key,
                    result.ErrorCode,
                    result.Retryable && result.ErrorCode is { } code && IsRetryableError(code));
    }

    private async Task<MacSourceTerminal> RunDictionarySourceAsync(
        IDictionaryProvider provider,
        string? dataFilePath,
        string text,
        Configuration configuration,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await RunDictionarySourceCoreAsync(
                provider,
                dataFilePath,
                text,
                configuration,
                generation,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MacSourceTerminal.Cancelled(provider.Registration.ProviderId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            PublishCurrentSourceResult(
                generation,
                provider.Registration.ProviderId,
                DescribeDictionaryProvider(provider),
                UiStrings.Get("mac.dictionary.unavailable"),
                UiStrings.Get("result.source.failed"));
            return MacSourceTerminal.Failed(
                provider.Registration.ProviderId,
                QueryErrorCode.Internal,
                retryable: false);
        }
    }

    private async Task<MacSourceTerminal> RunDictionarySourceCoreAsync(
        IDictionaryProvider provider,
        string? dataFilePath,
        string text,
        Configuration configuration,
        long generation,
        CancellationToken cancellationToken)
    {
        PublishCurrentSourceResult(
            generation,
            provider.Registration.ProviderId,
            DescribeDictionaryProvider(provider),
            string.Empty,
            UiStrings.Get("mac.source.looking_up"));
        var result = await provider.LookupAsync(text, dataFilePath, cancellationToken);
        PublishCurrentSourceResult(
            generation,
            provider.Registration.ProviderId,
            DescribeDictionaryProvider(provider),
            result.Entry?.ToDisplayText() ?? DescribeDictionaryStatus(result.Status),
            DescribeDictionarySourceStatus(result.Status),
            result.Entry?.Term);
        if (result.Succeeded)
        {
            await AppendDictionaryHistoryAsync(text, provider.Registration, result.Entry!, configuration);
            return MacSourceTerminal.Completed(provider.Registration.ProviderId);
        }

        if (result.Status == DictionaryLookupStatus.NotFound)
        {
            return MacSourceTerminal.Completed(provider.Registration.ProviderId);
        }

        return result.Status switch
        {
            DictionaryLookupStatus.Cancelled => MacSourceTerminal.Cancelled(provider.Registration.ProviderId),
            DictionaryLookupStatus.Unavailable => MacSourceTerminal.Failed(
                provider.Registration.ProviderId,
                QueryErrorCode.ProviderUnavailable,
                retryable: true),
            _ => MacSourceTerminal.Failed(provider.Registration.ProviderId, null, retryable: false),
        };
    }

    private async Task<TranslationSettingsResult> LoadTranslationSettingsAsync(
        ProviderDescriptor selectedProvider,
        ProviderSettingsDocument? providerDocument,
        Configuration configuration,
        CancellationToken cancellationToken)
    {
        if (providerDocument is null)
        {
            return TranslationSettingsResult.Failed(UiStrings.Get("mac.provider.configure"));
        }

        var profile = providerDocument.Profiles.FirstOrDefault(candidate => string.Equals(
            candidate.CanonicalProviderKey,
            CanonicalProviderKey(selectedProvider),
            StringComparison.Ordinal));
        if (profile is null)
        {
            return TranslationSettingsResult.Failed(UiStrings.Get("mac.provider.profile_unavailable"));
        }

        var definition = FindProviderDefinition(profile.Provider.ProviderId);
        if (definition is null)
        {
            return TranslationSettingsResult.Failed(UiStrings.Get("mac.provider.unsupported"));
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
            : TranslationSettingsResult.Failed(UiStrings.Get("mac.credential.unavailable"));
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
                : new MacSettingsSaveResult(false, UiStrings.Get("mac.credential.clear_failed"));
        }

        string? value = null;
        if (definition.CredentialKind == ProviderCredentialKind.VolcenginePair)
        {
            var hasPrimary = !string.IsNullOrWhiteSpace(input.Credential);
            var hasSecondary = !string.IsNullOrWhiteSpace(input.SecondaryCredential);
            if (hasPrimary != hasSecondary)
            {
                return new MacSettingsSaveResult(false, UiStrings.Get("mac.credential.pair_required"));
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
            : new MacSettingsSaveResult(false, UiStrings.Get("mac.credential.save_failed"));
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
            error = UiStrings.Get("mac.settings.choose_provider");
            return false;
        }

        if (!Uri.TryCreate(input.Endpoint, UriKind.Absolute, out var endpoint))
        {
            error = UiStrings.Get("mac.settings.endpoint_invalid");
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
            input.QuerySourceSettings.Validate();
            input.ProxySettings.Validate();
            input.HotkeySettings.Validate();
            input.HistoryRetention.Validate();
            if (FindProviderDefinition(input.ProviderId)!.ModelRequired && profile.Model is null)
            {
                error = UiStrings.Get("mac.settings.model_required");
                profile = null;
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            profile = null;
            error = UiStrings.Get("mac.settings.invalid_values");
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

    private async Task AppendDictionaryHistoryAsync(
        string text,
        DictionaryProviderRegistration registration,
        DictionaryLookupEntry entry,
        Configuration configuration)
    {
        try
        {
            var requestId = Guid.NewGuid().ToString("N");
            var provider = new ProviderDescriptor(registration.ProviderId);
            var definitions = new[] { entry.Translation, entry.Definition }
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!.Trim())
                .ToArray();
            var request = new QueryRequest(
                1,
                requestId,
                QueryKind.Dictionary,
                text,
                SourceLanguage: null,
                TargetLanguage: "und",
                provider);
            var result = new QueryResult(
                1,
                requestId,
                QueryKind.Dictionary,
                provider,
                QueryTerminalState.Completed,
                SourceLanguage: null,
                TargetLanguage: "und",
                new QueryResultPayload(
                    entry.ToDisplayText(),
                    [new DictionaryEntryResult(entry.Term, definitions)]));
            await _historyStore.AppendAsync(
                new HistoryEntry(1, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, request, result),
                configuration.HistoryRetention,
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Dictionary history cannot change a completed local lookup.
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
        lock (_stateGate)
        {
            _retry = null;
        }

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
        bool? canRetry = null,
        IReadOnlyList<TranslationResultViewModel>? results = null)
    {
        if (IsCurrent(generation))
        {
            PublishState(input, output, status, isBusy, canRetry, results);
        }
    }

    private void PublishState(
        string? input = null,
        string? output = null,
        string? status = null,
        bool? isBusy = null,
        bool? canRetry = null,
        IReadOnlyList<TranslationResultViewModel>? results = null)
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
                InputRevision = input is null ? _state.InputRevision : _state.InputRevision + 1,
                Output = output ?? _state.Output,
                Status = status ?? _state.Status,
                IsBusy = isBusy ?? _state.IsBusy,
                CanRetry = canRetry ?? _state.CanRetry,
                Results = results ?? _state.Results,
                Revision = _state.Revision + 1,
            };
            state = _state;
        }

        StateChanged?.Invoke(this, state);
    }

    private void PublishCurrentSourceResult(
        long generation,
        string key,
        string displayName,
        string text,
        string status,
        string? pronunciationTerm = null)
    {
        if (!IsCurrent(generation) || Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        MacRuntimeState state;
        lock (_stateGate)
        {
            if (!IsCurrent(generation))
            {
                return;
            }

            var results = _state.Results.ToList();
            var index = results.FindIndex(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
            var result = new TranslationResultViewModel(key, displayName, text, status, pronunciationTerm,
                index >= 0 ? results[index].TargetLanguage : null);
            if (index >= 0)
            {
                results[index] = result;
            }
            else
            {
                results.Add(result);
            }

            var combinedOutput = CombineResults(results);
            _state = _state with
            {
                Results = results,
                Output = combinedOutput,
                Revision = _state.Revision + 1,
            };
            state = _state;
        }

        StateChanged?.Invoke(this, state);
    }

    private IReadOnlyList<TranslationResultViewModel> PrepareRetryResults(
        IReadOnlyList<TranslationResultViewModel> retryPresentations)
    {
        lock (_stateGate)
        {
            var replacements = retryPresentations.ToDictionary(
                static result => result.Key,
                StringComparer.Ordinal);
            var results = new List<TranslationResultViewModel>(_state.Results.Count + replacements.Count);
            foreach (var result in _state.Results)
            {
                if (replacements.Remove(result.Key, out var replacement))
                {
                    results.Add(replacement);
                }
                else
                {
                    results.Add(result);
                }
            }

            results.AddRange(replacements.Values);
            return results;
        }
    }

    private static string CombineResults(IEnumerable<TranslationResultViewModel> results) => string.Join(
        Environment.NewLine + Environment.NewLine,
        results
            .Where(static result => !string.IsNullOrWhiteSpace(result.Text))
            .Select(static result => result.DisplayName + Environment.NewLine + result.Text));

    private void MarkActiveSourcesCancelled()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        MacRuntimeState state;
        lock (_stateGate)
        {
            var results = _state.Results
                .Select(static result => new[] { UiStrings.Get("result.source.waiting"), UiStrings.Get("result.source.receiving"), UiStrings.Get("mac.source.looking_up") }.Contains(result.Status, StringComparer.Ordinal)
                    ? result.WithStatus(UiStrings.Get("result.source.cancelled"))
                    : result)
                .ToArray();
            _retry = null;
            _state = _state with
            {
                Results = results,
                Status = UiStrings.Get("mac.status.cancelled"),
                IsBusy = false,
                CanRetry = false,
                Revision = _state.Revision + 1,
            };
            state = _state;
        }

        StateChanged?.Invoke(this, state);
    }

    private void HandleInputHotkeyPressed(object? sender, EventArgs eventArgs) =>
        InputPresentationRequested?.Invoke(this, EventArgs.Empty);

    private void HandleHotkeyPressed(object? sender, EventArgs eventArgs) => _ = Task.Run(
        () => TranslateSelectedTextAsync(promptForPermission: false),
        CancellationToken.None);

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
            UiStrings.Get("mac.selection.permission"),
        MacSelectionStatus.NoFocusedElement => UiStrings.Get("mac.selection.no_focused"),
        MacSelectionStatus.NoSelection => UiStrings.Get("mac.selection.no_selection"),
        MacSelectionStatus.Unsupported => UiStrings.Get("mac.selection.unsupported"),
        _ => UiStrings.Get("mac.selection.failed"),
    };

    private static string DescribeOcrFailure(MacOcrStatus status) => status switch
    {
        MacOcrStatus.NoText => UiStrings.Get("mac.ocr.no_text"),
        MacOcrStatus.LanguageUnavailable => UiStrings.Get("mac.ocr.language_unavailable"),
        MacOcrStatus.Cancelled => UiStrings.Get("mac.ocr.cancelled"),
        MacOcrStatus.Unsupported => UiStrings.Get("mac.ocr.unavailable"),
        _ => UiStrings.Get("mac.ocr.failed"),
    };

    private static string DescribeQueryError(QueryErrorCode errorCode) => errorCode switch
    {
        QueryErrorCode.InvalidRequest => UiStrings.Get("mac.translation.invalid"),
        QueryErrorCode.ProviderUnavailable => UiStrings.Get("mac.translation.unavailable"),
        QueryErrorCode.Timeout => UiStrings.Get("mac.translation.timeout"),
        QueryErrorCode.Network => UiStrings.Get("mac.translation.network"),
        QueryErrorCode.Authentication => UiStrings.Get("mac.translation.auth"),
        QueryErrorCode.RateLimited => UiStrings.Get("mac.translation.rate_limited"),
        QueryErrorCode.UnsupportedLanguage => UiStrings.Get("mac.translation.language"),
        _ => UiStrings.Get("mac.translation.failed"),
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

    private static string CanonicalProviderKey(ProviderDescriptor provider) => provider.InstanceId is null
        ? provider.ProviderId
        : provider.ProviderId + ":" + provider.InstanceId;

    private static string DescribeProvider(ProviderDescriptor provider)
    {
        var name = provider.ProviderId switch
        {
            TranslationProviderIds.OpenAiCompatible => "OpenAI-compatible",
            TranslationProviderIds.DeepL => "DeepL",
            TranslationProviderIds.Ollama => "Ollama",
            TranslationProviderIds.Bing => UiStrings.Get("provider.name.bing"),
            TranslationProviderIds.Google => UiStrings.Get("provider.name.google"),
            TranslationProviderIds.Volcengine => UiStrings.Get("provider.name.volcengine"),
            _ => provider.ProviderId,
        };
        return provider.InstanceId is null ? name : name + " (" + provider.InstanceId + ")";
    }

    private static string DescribeDictionaryProvider(IDictionaryProvider provider) =>
        provider.Registration.ProviderId == LocalDictionaryIds.MacSystem
            ? UiStrings.Get("result.source.mac_system_dictionary")
            : UiStrings.Get("result.source.local_dictionary");

    private static string DescribeSourceTerminal(TranslationStreamEventKind kind) => kind switch
    {
        TranslationStreamEventKind.Completed => string.Empty,
        TranslationStreamEventKind.Cancelled => UiStrings.Get("result.source.cancelled"),
        _ => UiStrings.Get("result.source.failed"),
    };

    private static string DescribeDictionarySourceStatus(DictionaryLookupStatus status) => status switch
    {
        DictionaryLookupStatus.Found => string.Empty,
        DictionaryLookupStatus.NotFound => UiStrings.Get("mac.source.no_entry"),
        DictionaryLookupStatus.Cancelled => UiStrings.Get("result.source.cancelled"),
        _ => UiStrings.Get("result.source.failed"),
    };

    private static string DescribeDictionaryStatus(DictionaryLookupStatus status) => status switch
    {
        DictionaryLookupStatus.NotFound => UiStrings.Get("dictionary.status.not_found"),
        DictionaryLookupStatus.InvalidRequest => UiStrings.Get("dictionary.status.invalid_request"),
        DictionaryLookupStatus.Unavailable => UiStrings.Get("mac.dictionary.unavailable"),
        DictionaryLookupStatus.InvalidData =>
            UiStrings.Get("dictionary.status.invalid_data"),
        DictionaryLookupStatus.Cancelled => UiStrings.Get("dictionary.status.cancelled"),
        _ => UiStrings.Get("mac.dictionary.unavailable"),
    };

    private static bool IsRetryableError(QueryErrorCode errorCode) => errorCode is
        QueryErrorCode.Timeout or
        QueryErrorCode.Network or
        QueryErrorCode.RateLimited or
        QueryErrorCode.ProviderUnavailable;

    private sealed record MacSourceTerminal(
        string Key,
        bool Succeeded,
        QueryErrorCode? ErrorCode,
        bool Retryable)
    {
        public static MacSourceTerminal Completed(string key) => new(key, true, null, false);

        public static MacSourceTerminal Cancelled(string key) => new(key, false, null, false);

        public static MacSourceTerminal Failed(
            string key,
            QueryErrorCode? errorCode,
            bool retryable) =>
            new(key, false, errorCode, retryable);
    }

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
