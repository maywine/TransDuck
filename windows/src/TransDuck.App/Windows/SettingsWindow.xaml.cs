// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TransDuck.App.Services;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Hotkeys;
using TransDuck.Infrastructure.Proxy;
using TransDuck.Platform.Windows.Startup;
using TransDuck.Infrastructure.Translation;

namespace TransDuck.App.Windows;

/// <summary>
/// Hosts the Windows MVP provider settings form without allowing code-behind to touch storage directly.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ProviderSettingsController _controller;
    private readonly QuerySourceSettingsController _querySourceController;
    private readonly ProxySettingsController _proxyController;
    private readonly HotkeySettingsController _hotkeyController;
    private readonly StartupSettingsController _startupController;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private IReadOnlyList<ProviderProfileSettings> _profiles = [];
    private Configuration? _configuration;
    private QuerySourceSettings? _querySources;
    private int _credentialStatusGeneration;
    private int _hotkeyStateGeneration;
    private int _proxyStateGeneration;
    private int _startupStateGeneration;
    private int _loadGeneration;
    private bool _isBusy;
    private bool _isClosed;
    private bool _isHotkeySaving;
    private bool _isProxySaving;
    private bool _isStartupSaving;
    private bool _isLoading;

    internal SettingsWindow(
        ProviderSettingsController controller,
        QuerySourceSettingsController querySourceController,
        ProxySettingsController proxyController,
        HotkeySettingsController hotkeyController,
        StartupSettingsController startupController)
    {
        _controller = controller;
        _querySourceController = querySourceController;
        _proxyController = proxyController;
        _hotkeyController = hotkeyController;
        _startupController = startupController;
        InitializeComponent();
        ProductVersionTextBlock.Text = TransDuck.Core.ProductVersionDisplay.FromAssembly(typeof(App).Assembly);
        Loaded += HandleLoaded;
        Closed += HandleClosed;
        _hotkeyController.StateChanged += HandleHotkeyStateChanged;
        _proxyController.StateChanged += HandleProxyStateChanged;
        _startupController.StateChanged += HandleStartupStateChanged;
        ApplyHotkeyControllerState();
        ApplyProxyControllerState();
        ApplyStartupControllerState();
    }

    private async void HandleLoaded(object sender, RoutedEventArgs eventArgs)
    {
        ApplyHotkeyControllerState();
        ApplyProxyControllerState();
        ApplyStartupControllerState();
        await Task.WhenAll(LoadAsync(), LoadProxyAsync(), LoadStartupAsync());
    }

    private async void ProviderSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isProxySaving || _isClosed)
        {
            return;
        }

        ApplySelectedProfile();
        await RefreshCredentialStatusAsync();
    }

    private async void SaveButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isProxySaving || _isClosed)
        {
            return;
        }

        if (!TryCreateSettings(out var profile, out var retention) ||
            !TryCreateQuerySourceSettings(profile, out var querySources))
        {
            SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.invalid_input");
            return;
        }

        if (!TryCreateCredentialValue(profile.Provider.ProviderId, out var password))
        {
            SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.invalid_input");
            return;
        }

        _isBusy = true;
        SetPersistenceControlsEnabled(false);
        try
        {
            var result = await _controller.SaveAsync(profile, retention, password, _lifetimeCancellation.Token);
            PersistenceResult? querySourceSave = null;
            if (result.Succeeded)
            {
                querySourceSave = await _querySourceController.SaveAsync(
                    querySources,
                    _lifetimeCancellation.Token);
            }
            if (!CanUpdateUi())
            {
                return;
            }

            SettingsStatusTextBlock.Text = querySourceSave is { Succeeded: false }
                ? AppStrings.Get("settings.status.sources_save_failed")
                : result.StatusMessage;
            if (result.RequiresSettingsReload)
            {
                var statusMessage = SettingsStatusTextBlock.Text;
                await LoadAsync(profile.Provider);
                if (CanUpdateUi())
                {
                    SettingsStatusTextBlock.Text = statusMessage;
                }
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (CanUpdateUi())
            {
                SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.save_failed");
            }
        }
        finally
        {
            CredentialPasswordBox.Clear();
            VolcengineAccessKeyIdPasswordBox.Clear();
            _isBusy = false;
            if (CanUpdateUi())
            {
                SetPersistenceControlsEnabled(true);
                ApplyHotkeyControllerState();
            }
        }
    }

    private void BrowseLocalDictionaryButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        var dialog = new OpenFileDialog
        {
            Title = AppStrings.Get("settings.label.local_dictionary_file"),
            CheckFileExists = true,
            Multiselect = false,
            Filter = "Local dictionary (*.csv;*.db;*.sqlite;*.sqlite3)|*.csv;*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            LocalDictionaryPathTextBox.Text = dialog.FileName;
            LocalDictionaryEnabledCheckBox.IsChecked = true;
        }
    }

    private async void SaveQuerySourcesButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isClosed ||
            !TryCreateQuerySourceSettings(currentProfile: null, out var querySources))
        {
            SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.invalid_input");
            return;
        }

        _isBusy = true;
        SetPersistenceControlsEnabled(false);
        try
        {
            var result = await _querySourceController.SaveAsync(
                querySources,
                _lifetimeCancellation.Token);
            if (CanUpdateUi())
            {
                if (result.Succeeded)
                {
                    _querySources = querySources;
                }

                SettingsStatusTextBlock.Text = result.Succeeded
                    ? AppStrings.Get("settings.status.sources_saved")
                    : AppStrings.Get("settings.status.sources_save_failed");
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (CanUpdateUi())
            {
                SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.sources_save_failed");
            }
        }
        finally
        {
            _isBusy = false;
            if (CanUpdateUi())
            {
                SetPersistenceControlsEnabled(true);
            }
        }
    }

    private async void ClearCredentialButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isProxySaving || _isClosed)
        {
            return;
        }

        if (!TryCreateProvider(out var provider))
        {
            SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.invalid_provider");
            return;
        }

        _isBusy = true;
        SetPersistenceControlsEnabled(false);
        try
        {
            var result = await _controller.ClearCredentialAsync(provider, _lifetimeCancellation.Token);
            if (!CanUpdateUi())
            {
                return;
            }

            SettingsStatusTextBlock.Text = result.StatusMessage;
            await RefreshCredentialStatusAsync();
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (CanUpdateUi())
            {
                SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.clear_failed");
            }
        }
        finally
        {
            CredentialPasswordBox.Clear();
            VolcengineAccessKeyIdPasswordBox.Clear();
            _isBusy = false;
            if (CanUpdateUi())
            {
                SetPersistenceControlsEnabled(true);
                ApplyHotkeyControllerState();
            }
        }
    }

    private void CloseButtonClick(object sender, RoutedEventArgs eventArgs) => Close();

    private async void SaveHotkeyButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isHotkeySaving || _isProxySaving || _isClosed ||
            !_hotkeyController.IsInitialized)
        {
            return;
        }

        if (!TryCreateHotkeySettings(out var settings))
        {
            HotkeyStatusTextBlock.Text = AppStrings.Get("hotkey.ui.invalid");
            return;
        }

        _isHotkeySaving = true;
        SetHotkeyControlsEnabled(false);
        try
        {
            var result = await _hotkeyController.SaveAsync(settings, _lifetimeCancellation.Token);
            if (CanUpdateUi())
            {
                ApplyHotkeySettings(_hotkeyController.CurrentSettings);
                HotkeyStatusTextBlock.Text = result.StatusMessage;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (CanUpdateUi())
            {
                HotkeyStatusTextBlock.Text = AppStrings.Get("hotkey.ui.save_failed");
            }
        }
        finally
        {
            _isHotkeySaving = false;
            if (CanUpdateUi())
            {
                ApplyHotkeyControllerState();
            }
        }
    }

    private void ProxyModeSelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isHotkeySaving || _isStartupSaving || _isProxySaving || _isClosed)
        {
            return;
        }

        ApplyProxyModeInputState();
    }

    private async void SaveProxySettingsButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isHotkeySaving || _isStartupSaving || _isProxySaving || _isClosed ||
            !_proxyController.IsInitialized)
        {
            return;
        }

        if (!TryCreateProxySettings(out var settings))
        {
            ProxyStatusTextBlock.Text = AppStrings.Get("proxy.ui.invalid");
            return;
        }

        _isProxySaving = true;
        SetProxyControlsEnabled(false);
        try
        {
            var result = await _proxyController.SaveAsync(settings, _lifetimeCancellation.Token);
            if (CanUpdateUi())
            {
                ApplyProxySettings(_proxyController.CurrentSettings);
                ProxyStatusTextBlock.Text = result.StatusMessage;
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (CanUpdateUi())
            {
                ProxyStatusTextBlock.Text = AppStrings.Get("proxy.save.failed");
            }
        }
        finally
        {
            _isProxySaving = false;
            if (CanUpdateUi())
            {
                ApplyProxyControllerState();
            }
        }
    }

    private async void SaveStartupButtonClick(object sender, RoutedEventArgs eventArgs)
    {
        if (_isLoading || _isBusy || _isHotkeySaving || _isProxySaving || _isStartupSaving || _isClosed ||
            !_startupController.IsInitialized)
        {
            return;
        }

        _isStartupSaving = true;
        SetStartupControlsEnabled(false);
        try
        {
            await _startupController.SetEnabledAsync(
                LaunchAtStartupCheckBox.IsChecked == true,
                _lifetimeCancellation.Token);
            if (CanUpdateUi())
            {
                ApplyStartupControllerState();
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (CanUpdateUi())
            {
                StartupStatusTextBlock.Text = AppStrings.Get("startup.status.failed");
            }
        }
        finally
        {
            _isStartupSaving = false;
            if (CanUpdateUi())
            {
                ApplyStartupControllerState();
            }
        }
    }

    private async Task LoadAsync(ProviderDescriptor? preferredProvider = null)
    {
        var generation = ++_loadGeneration;
        ++_credentialStatusGeneration;
        _isLoading = true;
        try
        {
            var loaded = await _controller.LoadAsync(_lifetimeCancellation.Token);
            if (!IsCurrentLoad(generation))
            {
                return;
            }

            _profiles = loaded.ProviderSettings.Profiles;
            _configuration = loaded.Configuration;
            var querySources = await _querySourceController.LoadAsync(
                loaded.Configuration.DefaultProvider,
                _lifetimeCancellation.Token);
            if (!IsCurrentLoad(generation))
            {
                return;
            }

            _querySources = querySources.Settings;
            ApplyQuerySourceSettings(querySources.Settings);
            if (querySources.UsesMigrationDefault &&
                !_profiles.Any(profile => string.Equals(
                    profile.CanonicalProviderKey,
                    CanonicalKey(loaded.Configuration.DefaultProvider),
                    StringComparison.Ordinal)))
            {
                foreach (var checkBox in SourceCheckBoxes())
                {
                    checkBox.IsChecked = false;
                }
            }
            var preferredProviderKey = preferredProvider is null
                ? null
                : CanonicalKey(preferredProvider);
            var preferredProfile = preferredProviderKey is not null
                ? _profiles.FirstOrDefault(profile => string.Equals(
                    profile.CanonicalProviderKey,
                    preferredProviderKey,
                    StringComparison.Ordinal))
                : null;
            var selectedProvider = preferredProfile?.Provider ?? loaded.Configuration.DefaultProvider;
            SelectProvider(selectedProvider.ProviderId);
            var selected = _profiles.FirstOrDefault(profile =>
                string.Equals(profile.CanonicalProviderKey, CanonicalKey(selectedProvider), StringComparison.Ordinal));
            ApplyProfile(selected ?? CreateDefaultProfile(selectedProvider.ProviderId), loaded.Configuration);
            var credentialStatus = selected is not null && !string.Equals(
                selected.CanonicalProviderKey,
                CanonicalKey(loaded.Configuration.DefaultProvider),
                StringComparison.Ordinal)
                ? await _controller.GetCredentialStatusAsync(selected.Provider, _lifetimeCancellation.Token)
                : loaded.CredentialStatus;
            if (!IsCurrentLoad(generation))
            {
                return;
            }

            CredentialStatusTextBlock.Text = DescribeCredentialStatus(selectedProvider, credentialStatus);
            SettingsStatusTextBlock.Text = loaded.StatusMessage;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (IsCurrentLoad(generation))
            {
                SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.load_failed");
            }
        }
        finally
        {
            if (IsCurrentLoad(generation))
            {
                _isLoading = false;
                ApplyHotkeyControllerState();
                ApplyProxyControllerState();
                ApplyStartupControllerState();
            }
        }
    }

    private async Task LoadStartupAsync()
    {
        var generation = ++_startupStateGeneration;
        try
        {
            await _startupController.InitializeAsync(_lifetimeCancellation.Token);
            if (IsCurrentStartupState(generation))
            {
                ApplyStartupControllerState(generation);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (IsCurrentStartupState(generation))
            {
                StartupStatusTextBlock.Text = AppStrings.Get("startup.status.failed");
            }
        }
    }

    private async Task LoadProxyAsync()
    {
        var generation = ++_proxyStateGeneration;
        try
        {
            await _proxyController.InitializeAsync(_lifetimeCancellation.Token);
            if (IsCurrentProxyState(generation))
            {
                ApplyProxyControllerState(generation);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (IsCurrentProxyState(generation))
            {
                ProxyStatusTextBlock.Text = AppStrings.Get("proxy.status.failed");
            }
        }
    }

    private async Task RefreshCredentialStatusAsync()
    {
        var generation = ++_credentialStatusGeneration;
        if (!TryCreateProvider(out var provider))
        {
            if (IsCurrentCredentialStatus(generation))
            {
                CredentialStatusTextBlock.Text = AppStrings.Get("provider.status.credential_unavailable");
            }

            return;
        }

        if (!UsesCredential(provider))
        {
            if (IsCurrentCredentialStatus(generation))
            {
                CredentialStatusTextBlock.Text = AppStrings.Get("provider.status.credential_not_required");
            }

            return;
        }

        try
        {
            var status = await _controller.GetCredentialStatusAsync(provider, _lifetimeCancellation.Token);
            if (IsCurrentCredentialStatus(generation))
            {
                CredentialStatusTextBlock.Text = DescribeCredentialStatus(provider, status);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (IsCurrentCredentialStatus(generation))
            {
                CredentialStatusTextBlock.Text = AppStrings.Get("provider.status.credential_unavailable");
            }
        }
    }

    private bool TryCreateSettings(
        out ProviderProfileSettings profile,
        out HistoryRetention retention)
    {
        profile = default!;
        retention = default!;
        if (!TryCreateProvider(out var provider) ||
            !Uri.TryCreate(EndpointTextBox.Text, UriKind.Absolute, out var endpoint) ||
            !int.TryParse(TimeoutSecondsTextBox.Text, out var timeoutSeconds) ||
            !int.TryParse(HistoryMaxEntriesTextBox.Text, out var maxEntries) ||
            !int.TryParse(HistoryMaxAgeDaysTextBox.Text, out var maxAgeDays))
        {
            return false;
        }

        profile = new ProviderProfileSettings(
            provider,
            endpoint,
            NullIfWhiteSpace(ModelTextBox.Text),
            NullIfWhiteSpace(SourceLanguageTextBox.Text),
            TargetLanguageTextBox.Text,
            timeoutSeconds);
        retention = new HistoryRetention(maxEntries, maxAgeDays);
        try
        {
            profile.Validate();
            retention.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private bool TryCreateQuerySourceSettings(
        ProviderProfileSettings? currentProfile,
        out QuerySourceSettings settings)
    {
        settings = default!;
        var providers = new List<ProviderDescriptor>();
        foreach (var checkBox in SourceCheckBoxes())
        {
            if (checkBox.IsChecked != true || checkBox.Tag is not string providerId)
            {
                continue;
            }

            var profile = currentProfile is not null && string.Equals(
                currentProfile.Provider.ProviderId,
                providerId,
                StringComparison.Ordinal)
                ? currentProfile
                : ResolveSelectedProfile(providerId);
            if (profile is null)
            {
                return false;
            }

            providers.Add(profile.Provider);
        }

        var localDictionaryEnabled = LocalDictionaryEnabledCheckBox.IsChecked == true;
        var localDictionaryPath = NullIfWhiteSpace(LocalDictionaryPathTextBox.Text);
        if (localDictionaryEnabled && (localDictionaryPath is null || !Path.IsPathFullyQualified(localDictionaryPath) || !File.Exists(localDictionaryPath)))
        {
            return false;
        }

        var candidate = new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            providers,
            new LocalDictionarySettings(localDictionaryEnabled, localDictionaryPath),
            MacSystemDictionaryEnabled: false);
        try
        {
            candidate.Validate();
            settings = candidate;
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private ProviderProfileSettings? ResolveSelectedProfile(string providerId)
    {
        var selected = _querySources?.EnabledTranslationProviders.FirstOrDefault(provider =>
            string.Equals(provider.ProviderId, providerId, StringComparison.Ordinal));
        if (selected is not null)
        {
            var canonicalKey = CanonicalKey(selected);
            var selectedProfile = _profiles.FirstOrDefault(profile => string.Equals(
                profile.CanonicalProviderKey,
                canonicalKey,
                StringComparison.Ordinal));
            if (selectedProfile is not null)
            {
                return selectedProfile;
            }
        }

        return _profiles.FirstOrDefault(profile => string.Equals(
            profile.Provider.ProviderId,
            providerId,
            StringComparison.Ordinal));
    }

    private bool TryCreateHotkeySettings(out HotkeySettings settings) =>
        HotkeySettingsController.TryCreateSettings(
            ControlHotkeyModifierCheckBox.IsChecked == true,
            AltHotkeyModifierCheckBox.IsChecked == true,
            ShiftHotkeyModifierCheckBox.IsChecked == true,
            WindowsHotkeyModifierCheckBox.IsChecked == true,
            HotkeyKeyTextBox.Text,
            out settings);

    private bool TryCreateProxySettings(out ProxySettings settings)
    {
        settings = ProxySettings.Default;
        if (ProxyModeComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag ||
            !Enum.TryParse<ProxyMode>(tag, ignoreCase: false, out var mode) ||
            !Enum.IsDefined(mode))
        {
            return false;
        }

        Uri? customProxyUri = null;
        if (mode == ProxyMode.CustomHttp &&
            !Uri.TryCreate(CustomHttpProxyUriTextBox.Text, UriKind.Absolute, out customProxyUri))
        {
            return false;
        }

        var candidate = new ProxySettings(
            ProxySettingsMigration.CurrentVersion,
            mode,
            customProxyUri);
        try
        {
            candidate.Validate();
            settings = candidate;
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private bool TryCreateProvider(out ProviderDescriptor provider)
    {
        provider = default!;
        if (ProviderComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string providerId)
        {
            return false;
        }

        provider = new ProviderDescriptor(providerId, NullIfWhiteSpace(InstanceIdTextBox.Text));
        try
        {
            provider.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private void ApplySelectedProfile()
    {
        if (ProviderComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string providerId)
        {
            return;
        }

        var profile = _profiles.FirstOrDefault(profile =>
            string.Equals(profile.Provider.ProviderId, providerId, StringComparison.Ordinal));
        ApplyProfile(profile ?? CreateDefaultProfile(providerId), _configuration);
    }

    private void ApplyQuerySourceSettings(QuerySourceSettings settings)
    {
        var enabledProviderIds = settings.EnabledTranslationProviders
            .Select(static provider => provider.ProviderId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var checkBox in SourceCheckBoxes())
        {
            checkBox.IsChecked = checkBox.Tag is string providerId && enabledProviderIds.Contains(providerId);
        }

        LocalDictionaryEnabledCheckBox.IsChecked = settings.LocalDictionary.Enabled;
        LocalDictionaryPathTextBox.Text = settings.LocalDictionary.DataFilePath ?? string.Empty;
    }

    private void ApplyProfile(ProviderProfileSettings? profile, Configuration? configuration)
    {
        InstanceIdTextBox.Text = profile?.Provider.InstanceId ?? string.Empty;
        EndpointTextBox.Text = profile?.Endpoint.AbsoluteUri ?? string.Empty;
        ModelTextBox.Text = profile?.Model ?? string.Empty;
        SourceLanguageTextBox.Text = profile?.SourceLanguage ?? string.Empty;
        TargetLanguageTextBox.Text = profile?.TargetLanguage ?? "zh-Hans";
        TimeoutSecondsTextBox.Text = (profile?.TimeoutSeconds ?? 30).ToString();
        HistoryMaxEntriesTextBox.Text = (configuration?.HistoryRetention.MaxEntries ?? 100).ToString();
        HistoryMaxAgeDaysTextBox.Text = (configuration?.HistoryRetention.MaxAgeDays ?? 30).ToString();
        ApplyCredentialLayout();
        ApplyCredentialControlsEnabledState();
    }

    private static ProviderProfileSettings? CreateDefaultProfile(string providerId)
    {
        var endpoint = providerId switch
        {
            TranslationProviderIds.Bing => BingWebProvider.DefaultEndpoint,
            TranslationProviderIds.Google => GoogleWebProvider.DefaultEndpoint,
            TranslationProviderIds.Volcengine => VolcengineProvider.DefaultEndpoint,
            _ => null,
        };
        return endpoint is null
            ? null
            : new ProviderProfileSettings(
                new ProviderDescriptor(providerId),
                new Uri(endpoint, UriKind.Absolute),
                Model: null,
                SourceLanguage: null,
                TargetLanguage: "zh-Hans",
                TimeoutSeconds: 30);
    }

    private void SelectProvider(string providerId)
    {
        foreach (var candidate in ProviderComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(candidate.Tag as string, providerId, StringComparison.Ordinal))
            {
                ProviderComboBox.SelectedItem = candidate;
                return;
            }
        }

        ProviderComboBox.SelectedIndex = 0;
    }

    private void HandleClosed(object? sender, EventArgs eventArgs)
    {
        _isClosed = true;
        ++_loadGeneration;
        ++_credentialStatusGeneration;
        ++_hotkeyStateGeneration;
        ++_proxyStateGeneration;
        ++_startupStateGeneration;
        _lifetimeCancellation.Cancel();
        _hotkeyController.StateChanged -= HandleHotkeyStateChanged;
        _proxyController.StateChanged -= HandleProxyStateChanged;
        _startupController.StateChanged -= HandleStartupStateChanged;
    }

    private bool CanUpdateUi() => !_isClosed && !_lifetimeCancellation.IsCancellationRequested;

    private bool IsCurrentLoad(int generation) => CanUpdateUi() && _loadGeneration == generation;

    private bool IsCurrentCredentialStatus(int generation) =>
        CanUpdateUi() && _credentialStatusGeneration == generation;

    private bool IsCurrentHotkeyState(int generation) =>
        CanUpdateUi() && _hotkeyStateGeneration == generation;

    private bool IsCurrentProxyState(int generation) =>
        CanUpdateUi() && _proxyStateGeneration == generation;

    private bool IsCurrentStartupState(int generation) =>
        CanUpdateUi() && _startupStateGeneration == generation;

    private void SetPersistenceControlsEnabled(bool isEnabled)
    {
        ProviderComboBox.IsEnabled = isEnabled;
        foreach (var checkBox in SourceCheckBoxes())
        {
            checkBox.IsEnabled = isEnabled;
        }

        LocalDictionaryEnabledCheckBox.IsEnabled = isEnabled;
        LocalDictionaryPathTextBox.IsEnabled = isEnabled;
        BrowseLocalDictionaryButton.IsEnabled = isEnabled;
        SaveQuerySourcesButton.IsEnabled = isEnabled;
        SaveProviderSettingsButton.IsEnabled = isEnabled;
        SetCredentialControlsEnabled(isEnabled && SelectedProviderUsesCredential());
        SetProxyControlsEnabled(
            isEnabled && _proxyController.IsInitialized && !_isProxySaving && CanUpdateUi());
        SetStartupControlsEnabled(
            isEnabled && _startupController.IsInitialized && !_isStartupSaving && !_isLoading && !_isBusy && CanUpdateUi());
    }

    private void HandleHotkeyStateChanged(object? sender, EventArgs eventArgs)
    {
        var generation = ++_hotkeyStateGeneration;
        if (!CanUpdateUi())
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyHotkeyControllerState(generation);
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyHotkeyControllerState(generation)));
        }
        catch (InvalidOperationException)
        {
            // A window closing concurrently with startup must not receive late state updates.
        }
    }

    private void HandleProxyStateChanged(object? sender, EventArgs eventArgs)
    {
        var generation = ++_proxyStateGeneration;
        if (!CanUpdateUi())
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyProxyControllerState(generation);
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyProxyControllerState(generation)));
        }
        catch (InvalidOperationException)
        {
            // A closing settings window must not receive a late proxy-state update.
        }
    }

    private void ApplyHotkeyControllerState(int? generation = null)
    {
        if (generation is { } value && !IsCurrentHotkeyState(value))
        {
            return;
        }

        ApplyHotkeySettings(_hotkeyController.CurrentSettings);
        HotkeyStatusTextBlock.Text = _hotkeyController.StatusMessage;
        SetHotkeyControlsEnabled(
            _hotkeyController.IsInitialized && !_isHotkeySaving && !_isLoading && !_isBusy && CanUpdateUi());
    }

    private void ApplyProxyControllerState(int? generation = null)
    {
        if (generation is { } value && !IsCurrentProxyState(value))
        {
            return;
        }

        ApplyProxySettings(_proxyController.CurrentSettings);
        ProxyStatusTextBlock.Text = _proxyController.StatusMessage;
        SetProxyControlsEnabled(
            _proxyController.IsInitialized && !_isBusy && !_isHotkeySaving && !_isStartupSaving &&
            !_isProxySaving && CanUpdateUi());
    }

    private void HandleStartupStateChanged(object? sender, EventArgs eventArgs)
    {
        var generation = ++_startupStateGeneration;
        if (!CanUpdateUi())
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyStartupControllerState(generation);
            return;
        }

        try
        {
            _ = Dispatcher.BeginInvoke(new Action(() => ApplyStartupControllerState(generation)));
        }
        catch (InvalidOperationException)
        {
            // A closing settings window must not receive a late registry-state update.
        }
    }

    private void ApplyStartupControllerState(int? generation = null)
    {
        if (generation is { } value && !IsCurrentStartupState(value))
        {
            return;
        }

        LaunchAtStartupCheckBox.IsChecked = _startupController.IsEnabled;
        StartupStatusTextBlock.Text = _startupController.StatusMessage;
        SetStartupControlsEnabled(
            _startupController.IsInitialized && !_isStartupSaving && !_isLoading && !_isBusy && CanUpdateUi());
    }

    private void ApplyHotkeySettings(HotkeySettings settings)
    {
        ControlHotkeyModifierCheckBox.IsChecked = settings.Control;
        AltHotkeyModifierCheckBox.IsChecked = settings.Alt;
        ShiftHotkeyModifierCheckBox.IsChecked = settings.Shift;
        WindowsHotkeyModifierCheckBox.IsChecked = settings.Windows;
        HotkeyKeyTextBox.Text = HotkeySettingsController.DescribeVirtualKey(settings.VirtualKey);
    }

    private void SetHotkeyControlsEnabled(bool isEnabled)
    {
        ControlHotkeyModifierCheckBox.IsEnabled = isEnabled;
        AltHotkeyModifierCheckBox.IsEnabled = isEnabled;
        ShiftHotkeyModifierCheckBox.IsEnabled = isEnabled;
        WindowsHotkeyModifierCheckBox.IsEnabled = isEnabled;
        HotkeyKeyTextBox.IsEnabled = isEnabled;
        SaveHotkeyButton.IsEnabled = isEnabled;
    }

    private void SetStartupControlsEnabled(bool isEnabled)
    {
        LaunchAtStartupCheckBox.IsEnabled = isEnabled;
        SaveStartupButton.IsEnabled = isEnabled;
    }

    private void ApplyProxySettings(ProxySettings settings)
    {
        SelectProxyMode(settings.Mode);
        CustomHttpProxyUriTextBox.Text = settings.CustomHttpProxyUri?.OriginalString ?? string.Empty;
        ApplyProxyModeInputState();
    }

    private void SelectProxyMode(ProxyMode mode)
    {
        foreach (var candidate in ProxyModeComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(candidate.Tag as string, mode.ToString(), StringComparison.Ordinal))
            {
                ProxyModeComboBox.SelectedItem = candidate;
                return;
            }
        }

        ProxyModeComboBox.SelectedIndex = 0;
    }

    private void ApplyProxyModeInputState() =>
        SetProxyControlsEnabled(
            _proxyController.IsInitialized && !_isBusy && !_isHotkeySaving && !_isStartupSaving &&
            !_isProxySaving && CanUpdateUi());

    private void SetProxyControlsEnabled(bool isEnabled)
    {
        var customMode = ProxyModeComboBox.SelectedItem is ComboBoxItem item &&
            string.Equals(item.Tag as string, nameof(ProxyMode.CustomHttp), StringComparison.Ordinal);
        ProxyModeComboBox.IsEnabled = isEnabled;
        CustomHttpProxyUriTextBox.IsEnabled = isEnabled && customMode;
        SaveProxySettingsButton.IsEnabled = isEnabled;
        if (!customMode)
        {
            CustomHttpProxyUriTextBox.Clear();
        }
    }

    private void ApplyCredentialControlsEnabledState() =>
        SetCredentialControlsEnabled(!_isBusy && CanUpdateUi() && SelectedProviderUsesCredential());

    private void SetCredentialControlsEnabled(bool isEnabled)
    {
        var usesVolcengineKeyPair = SelectedProviderIsVolcengine();
        VolcengineAccessKeyIdPasswordBox.IsEnabled = isEnabled && usesVolcengineKeyPair;
        CredentialPasswordBox.IsEnabled = isEnabled;
        ClearCredentialButton.IsEnabled = isEnabled;
        if (!isEnabled)
        {
            CredentialPasswordBox.Clear();
            VolcengineAccessKeyIdPasswordBox.Clear();
        }
        else if (!usesVolcengineKeyPair)
        {
            VolcengineAccessKeyIdPasswordBox.Clear();
        }
    }

    private void ApplyCredentialLayout()
    {
        var usesVolcengineKeyPair = SelectedProviderIsVolcengine();
        VolcengineAccessKeyPanel.Visibility = usesVolcengineKeyPair
            ? Visibility.Visible
            : Visibility.Collapsed;
        CredentialLabelTextBlock.Text = AppStrings.Get(usesVolcengineKeyPair
            ? "settings.label.volcengine_secret_access_key"
            : "settings.label.credential");
    }

    private bool TryCreateCredentialValue(string providerId, out string? value)
    {
        value = null;
        if (!string.Equals(providerId, TranslationProviderIds.Volcengine, StringComparison.Ordinal))
        {
            value = string.IsNullOrEmpty(CredentialPasswordBox.Password)
                ? null
                : CredentialPasswordBox.Password;
            return true;
        }

        var accessKeyId = VolcengineAccessKeyIdPasswordBox.Password;
        var secretAccessKey = CredentialPasswordBox.Password;
        if (string.IsNullOrEmpty(accessKeyId) && string.IsNullOrEmpty(secretAccessKey))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            return false;
        }

        value = VolcengineCredentialCodec.Encode(accessKeyId, secretAccessKey);
        return true;
    }

    private bool SelectedProviderIsVolcengine() =>
        ProviderComboBox.SelectedItem is ComboBoxItem item &&
        string.Equals(item.Tag as string, TranslationProviderIds.Volcengine, StringComparison.Ordinal);

    private bool SelectedProviderUsesCredential() =>
        ProviderComboBox.SelectedItem is not ComboBoxItem item ||
        item.Tag is not string providerId ||
        !string.Equals(providerId, TranslationProviderIds.Google, StringComparison.Ordinal);

    private static bool UsesCredential(ProviderDescriptor provider) =>
        !string.Equals(provider.ProviderId, TranslationProviderIds.Google, StringComparison.Ordinal);

    private static string DescribeCredentialStatus(
        ProviderDescriptor provider,
        TransDuck.Core.Persistence.PersistenceStatus status) =>
        !UsesCredential(provider)
            ? AppStrings.Get("provider.status.credential_not_required")
            : status switch
            {
                TransDuck.Core.Persistence.PersistenceStatus.Succeeded => AppStrings.Get("provider.status.credential_saved"),
                TransDuck.Core.Persistence.PersistenceStatus.NotFound => AppStrings.Get("provider.status.credential_not_found"),
                TransDuck.Core.Persistence.PersistenceStatus.Cancelled => AppStrings.Get("settings.status.credential_cancelled"),
                _ => AppStrings.Get("provider.status.credential_unavailable"),
            };

    private static string CanonicalKey(ProviderDescriptor provider) => provider.InstanceId is null
        ? provider.ProviderId
        : provider.ProviderId + ":" + provider.InstanceId;

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private IReadOnlyList<CheckBox> SourceCheckBoxes() =>
    [
        OpenAiSourceCheckBox,
        DeepLSourceCheckBox,
        OllamaSourceCheckBox,
        BingSourceCheckBox,
        GoogleSourceCheckBox,
        VolcengineSourceCheckBox,
    ];
}
