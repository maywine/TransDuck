// Copyright (c) 2026 maywine. All rights reserved.

using System.Windows;
using System.Windows.Controls;
using TransDuck.App.Services;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Hotkeys;
using TransDuck.Platform.Windows.Proxy;
using TransDuck.Platform.Windows.Startup;
using TransDuck.Platform.Windows.Translation;

namespace TransDuck.App.Windows;

/// <summary>
/// Hosts the Windows MVP provider settings form without allowing code-behind to touch storage directly.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly ProviderSettingsController _controller;
    private readonly ProxySettingsController _proxyController;
    private readonly HotkeySettingsController _hotkeyController;
    private readonly StartupSettingsController _startupController;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private IReadOnlyList<ProviderProfileSettings> _profiles = [];
    private Configuration? _configuration;
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
        ProxySettingsController proxyController,
        HotkeySettingsController hotkeyController,
        StartupSettingsController startupController)
    {
        _controller = controller;
        _proxyController = proxyController;
        _hotkeyController = hotkeyController;
        _startupController = startupController;
        InitializeComponent();
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

        if (!TryCreateSettings(out var profile, out var retention))
        {
            SettingsStatusTextBlock.Text = AppStrings.Get("settings.status.invalid_input");
            return;
        }

        var password = CredentialPasswordBox.Password;
        _isBusy = true;
        SetPersistenceControlsEnabled(false);
        try
        {
            var result = await _controller.SaveAsync(profile, retention, password, _lifetimeCancellation.Token);
            if (!CanUpdateUi())
            {
                return;
            }

            SettingsStatusTextBlock.Text = result.StatusMessage;
            if (result.RequiresSettingsReload)
            {
                var statusMessage = result.StatusMessage;
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
            _isBusy = false;
            if (CanUpdateUi())
            {
                SetPersistenceControlsEnabled(true);
                ApplyHotkeyControllerState();
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

    private bool TryCreateHotkeySettings(out HotkeySettings settings) =>
        HotkeySettingsController.TryCreateSettings(
            ControlHotkeyModifierCheckBox.IsChecked == true,
            AltHotkeyModifierCheckBox.IsChecked == true,
            ShiftHotkeyModifierCheckBox.IsChecked == true,
            WindowsHotkeyModifierCheckBox.IsChecked == true,
            HotkeyKeyTextBox.Text,
            out settings);

    private bool TryCreateProxySettings(out WindowsProxySettings settings)
    {
        settings = WindowsProxySettings.Default;
        if (ProxyModeComboBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string tag ||
            !Enum.TryParse<WindowsProxyMode>(tag, ignoreCase: false, out var mode) ||
            !Enum.IsDefined(mode))
        {
            return false;
        }

        Uri? customProxyUri = null;
        if (mode == WindowsProxyMode.CustomHttp &&
            !Uri.TryCreate(CustomHttpProxyUriTextBox.Text, UriKind.Absolute, out customProxyUri))
        {
            return false;
        }

        var candidate = new WindowsProxySettings(
            WindowsProxySettingsMigration.CurrentVersion,
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
        ApplyCredentialControlsEnabledState();
    }

    private static ProviderProfileSettings? CreateDefaultProfile(string providerId)
    {
        var endpoint = providerId switch
        {
            TranslationProviderIds.Bing => BingWebProvider.DefaultEndpoint,
            TranslationProviderIds.Google => GoogleWebProvider.DefaultEndpoint,
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

    private void ApplyProxySettings(WindowsProxySettings settings)
    {
        SelectProxyMode(settings.Mode);
        CustomHttpProxyUriTextBox.Text = settings.CustomHttpProxyUri?.OriginalString ?? string.Empty;
        ApplyProxyModeInputState();
    }

    private void SelectProxyMode(WindowsProxyMode mode)
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
            string.Equals(item.Tag as string, nameof(WindowsProxyMode.CustomHttp), StringComparison.Ordinal);
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
        CredentialPasswordBox.IsEnabled = isEnabled;
        ClearCredentialButton.IsEnabled = isEnabled;
        if (!isEnabled)
        {
            CredentialPasswordBox.Clear();
        }
    }

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
}
