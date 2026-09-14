using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TransDuck.Core;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Proxy;
using TransDuck.Platform.MacOS.Hotkeys;
using TransDuck.Platform.MacOS.Startup;
using TransDuck.UI;
using TransDuck.UI.Views;

namespace TransDuck.MacOS.App.Views;

internal sealed class SettingsWindow : SettingsWindowBase
{
    private readonly MacAppRuntime _runtime;
    private readonly Dictionary<string, ProviderProfileSettings> _profiles = new(StringComparer.Ordinal);
    private bool _loading;
    private bool _allowClose;
    private int _credentialStatusGeneration;

    public SettingsWindow(MacAppRuntime runtime)
    {
        _runtime = runtime;
        ConfigureForMacSettingsWindow();
        ProviderSelectionRequested += HandleProviderSelectionChanged;
        ProxyModeSelectionRequested += HandleProxyModeSelectionChanged;
        AccessibilityRequested += HandleAccessibilityRequested;
        ReloadRequested += HandleReloadRequested;
        BrowseLocalDictionaryRequested += HandleBrowseLocalDictionaryRequested;
        SaveQuerySourcesRequested += HandleSaveQuerySourcesRequested;
        SaveAllRequested += HandleSaveRequested;
        SaveInputHotkeyRequested += HandleSaveInputHotkeyRequested;
        VersionTextBlock.Text = ProductVersionDisplay.FromAssembly(typeof(App).Assembly);
        foreach (var key in Enum.GetValues<MacVirtualKey>())
        {
            HotkeyKeyComboBox.Items.Add(new ComboBoxItem { Content = DescribeKey(key), Tag = key });
            InputHotkeyKeyComboBox.Items.Add(new ComboBoxItem { Content = DescribeKey(key), Tag = key });
        }

        Opened += HandleOpened;
        Closing += HandleClosing;
    }

    private void HandleOpened(object? sender, EventArgs eventArgs) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        SetFormBusy(true);
        SaveButton.IsEnabled = false;
        StatusTextBlock.Text = UiStrings.Get("mac.settings.loading");
        try
        {
            var snapshot = await _runtime.LoadSettingsAsync(CancellationToken.None);
            _profiles.Clear();
            foreach (var profile in snapshot.Profiles)
            {
                _profiles[profile.Provider.ProviderId] = profile;
            }

            SelectByTag(ProviderComboBox, snapshot.Configuration.DefaultProvider.ProviderId, fallbackIndex: 0);
            ApplySelectedProvider();
            ApplyQuerySourceSettings(snapshot.QuerySourceSettings);
            if (snapshot.QuerySourceSettingsStatus == PersistenceStatus.NotFound &&
                !_profiles.ContainsKey(snapshot.Configuration.DefaultProvider.ProviderId))
            {
                foreach (var checkBox in SourceCheckBoxes())
                {
                    checkBox.IsChecked = false;
                }
            }
            SelectByTag(ProxyModeComboBox, snapshot.ProxySettings.Mode.ToString(), fallbackIndex: 0);
            ProxyUriTextBox.Text = snapshot.ProxySettings.CustomHttpProxyUri?.OriginalString ?? string.Empty;
            ApplyProxyInputState();
            ApplyHotkey(snapshot.HotkeySettings);
            ApplyInputHotkey(snapshot.InputHotkeySettings);
            InputHotkeyStatusTextBlock.Text = snapshot.InputHotkeySettings.UsesSameChord(snapshot.HotkeySettings)
                ? UiStrings.Get("hotkey.input.conflict") : string.Empty;
            MaxEntriesNumericUpDown.Value = snapshot.Configuration.HistoryRetention.MaxEntries;
            MaxAgeNumericUpDown.Value = snapshot.Configuration.HistoryRetention.MaxAgeDays;
            StartAtLoginCheckBox.IsChecked = snapshot.StartupResult.IsEnabled;
            StatusTextBlock.Text = snapshot.StartupResult.Status == MacStartupStatus.Conflict
                ? UiStrings.Get("mac.settings.startup_conflict")
                : UiStrings.Get("mac.settings.loaded");
            await RefreshCredentialStatusAsync();
        }
        finally
        {
            _loading = false;
            SaveButton.IsEnabled = true;
            ProviderComboBox.IsEnabled = true;
            SetFormBusy(false);
        }
    }

    private void HandleProviderSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs)
    {
        if (_loading)
        {
            return;
        }

        ApplySelectedProvider();
        _ = RefreshCredentialStatusAsync();
    }

    private void ApplySelectedProvider()
    {
        BeginProviderChange();
        var providerId = SelectedProviderId();
        var definition = MacAppRuntime.ProviderDefinitions.First(candidate => candidate.ProviderId == providerId);
        if (_profiles.TryGetValue(providerId, out var profile))
        {
            EndpointTextBox.Text = profile.Endpoint.OriginalString;
            ModelTextBox.Text = profile.Model ?? string.Empty;
            SourceLanguageTextBox.Text = profile.SourceLanguage ?? string.Empty;
            TargetLanguageTextBox.Text = profile.TargetLanguage;
            TimeoutNumericUpDown.Value = profile.TimeoutSeconds;
        }
        else
        {
            EndpointTextBox.Text = definition.DefaultEndpoint;
            ModelTextBox.Text = string.Empty;
            SourceLanguageTextBox.Text = string.Empty;
            TargetLanguageTextBox.Text = "zh-Hans";
            TimeoutNumericUpDown.Value = 45;
        }

        CredentialTextBox.Text = string.Empty;
        SecondaryCredentialTextBox.Text = string.Empty;
        ClearCredentialCheckBox.IsChecked = false;
        var pair = definition.CredentialKind == ProviderCredentialKind.VolcenginePair;
        SecondaryCredentialLabel.IsVisible = pair;
        SecondaryCredentialTextBox.IsVisible = pair;
        CredentialLabel.Text = definition.CredentialKind switch
        {
            ProviderCredentialKind.None => UiStrings.Get("mac.credential.none_label"),
            ProviderCredentialKind.Optional when providerId == TranslationProviderIds.Bing =>
                UiStrings.Get("mac.credential.bing_label"),
            ProviderCredentialKind.Optional => UiStrings.Get("mac.credential.optional_label"),
            ProviderCredentialKind.VolcenginePair => UiStrings.Get("mac.credential.volcengine_label"),
            _ => "API Key",
        };
        var credentialEnabled = definition.CredentialKind != ProviderCredentialKind.None;
        CredentialTextBox.IsEnabled = credentialEnabled;
        SecondaryCredentialTextBox.IsEnabled = credentialEnabled;
        ClearCredentialCheckBox.IsEnabled = credentialEnabled;
        CompleteProviderChange(providerId);
    }

    private async Task RefreshCredentialStatusAsync()
    {
        var generation = ++_credentialStatusGeneration;
        var definition = MacAppRuntime.ProviderDefinitions.First(candidate =>
            candidate.ProviderId == SelectedProviderId());
        if (definition.CredentialKind == ProviderCredentialKind.None)
        {
            CredentialStatusTextBlock.Text = UiStrings.Get("provider.status.credential_not_required");
            return;
        }

        var status = await _runtime.GetCredentialStatusAsync(definition.ProviderId, CancellationToken.None);
        if (generation != _credentialStatusGeneration) return;
        CredentialStatusTextBlock.Text = status switch
        {
            PersistenceStatus.Succeeded => UiStrings.Get("mac.credential.saved"),
            PersistenceStatus.NotFound => UiStrings.Get("mac.credential.not_found"),
            _ => UiStrings.Get("mac.credential.status_unavailable"),
        };
    }

    private void HandleProxyModeSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) =>
        ApplyProxyInputState();

    private void ApplyProxyInputState() => ProxyUriTextBox.IsEnabled =
        string.Equals(SelectedTag(ProxyModeComboBox), nameof(ProxyMode.CustomHttp), StringComparison.Ordinal);

    private async void HandleAccessibilityRequested(object? sender, EventArgs eventArgs)
    {
        var ready = await _runtime.EnsureAccessibilityAndHotkeyAsync(prompt: true);
        StatusTextBlock.Text = ready
            ? UiStrings.Get("mac.status.accessibility_ready")
            : UiStrings.Get("mac.settings.accessibility_pending");
    }

    private void HandleReloadRequested(object? sender, EventArgs eventArgs)
    {
        ClearProviderDrafts();
        _ = LoadAsync();
    }

    private async void HandleBrowseLocalDictionaryRequested(object? sender, EventArgs eventArgs)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = UiStrings.Get("mac.dictionary.choose"),
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(UiStrings.Get("mac.dictionary.file_type"))
                    {
                        Patterns = ["*.csv", "*.db", "*.sqlite", "*.sqlite3"],
                    },
                    FilePickerFileTypes.All,
                ],
            });
            if (files.Count > 0)
            {
                LocalDictionaryPathTextBox.Text = files[0].Path.LocalPath;
                LocalDictionaryEnabledCheckBox.IsChecked = true;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            StatusTextBlock.Text = UiStrings.Get("mac.dictionary.select_failed");
        }
    }

    private async void HandleSaveQuerySourcesRequested(object? sender, EventArgs eventArgs)
    {
        if (!TryCreateQuerySourceSettings(includeCurrentUnsavedProvider: false, out var settings, out var error))
        {
            StatusTextBlock.Text = error;
            return;
        }

        SaveQuerySourcesButton.IsEnabled = false;
        try
        {
            var result = await _runtime.SaveQuerySourcesAsync(settings, CancellationToken.None);
            StatusTextBlock.Text = result.Message;
        }
        finally
        {
            SaveQuerySourcesButton.IsEnabled = true;
        }
    }

    private async void HandleSaveRequested(object? sender, EventArgs eventArgs)
    {
        SaveButton.IsEnabled = false;
        ProviderComboBox.IsEnabled = false;
        SetFormBusy(true);
        try
        {
            if (!TryCreateInput(out var input, out var error))
            {
                StatusTextBlock.Text = error;
                return;
            }

            var result = await _runtime.SaveSettingsAsync(input!, CancellationToken.None);
            StatusTextBlock.Text = result.Message;
            if (result.Succeeded)
            {
                AcceptCurrentProviderDraft();
                await LoadAsync();
            }
        }
        finally
        {
            SaveButton.IsEnabled = true;
            ProviderComboBox.IsEnabled = true;
            SetFormBusy(false);
        }
    }

    private async void HandleSaveInputHotkeyRequested(object? sender, EventArgs eventArgs)
    {
        var modifiers = MacHotkeyModifiers.None;
        if (InputCommandCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Command;
        if (InputOptionCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Option;
        if (InputControlCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Control;
        if (InputShiftCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Shift;
        if ((InputHotkeyKeyComboBox.SelectedItem as ComboBoxItem)?.Tag is not MacVirtualKey key)
        {
            InputHotkeyStatusTextBlock.Text = UiStrings.Get("mac.settings.hotkey_choose");
            return;
        }

        SetFormBusy(true);
        try
        {
            var result = await _runtime.SaveInputHotkeyAsync(
                new MacHotkeySettings(MacHotkeySettingsMigration.CurrentVersion, modifiers, key),
                CancellationToken.None);
            InputHotkeyStatusTextBlock.Text = result.Message;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            InputHotkeyStatusTextBlock.Text = UiStrings.Get("hotkey.input.save_failed");
        }
        finally { SetFormBusy(false); }
    }

    private void ApplyInputHotkey(MacHotkeySettings settings)
    {
        InputCommandCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Command);
        InputOptionCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Option);
        InputControlCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Control);
        InputShiftCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Shift);
        SelectByTag(InputHotkeyKeyComboBox, settings.Key, fallbackIndex: 19);
    }

    private bool TryCreateInput(out MacSettingsInput? input, out string error)
    {
        input = null;
        error = string.Empty;
        if (!Enum.TryParse<ProxyMode>(SelectedTag(ProxyModeComboBox), out var proxyMode))
        {
            error = UiStrings.Get("mac.settings.proxy_choose");
            return false;
        }

        Uri? proxyUri = null;
        if (proxyMode == ProxyMode.CustomHttp &&
            !Uri.TryCreate(ProxyUriTextBox.Text, UriKind.Absolute, out proxyUri))
        {
            error = UiStrings.Get("mac.settings.proxy_invalid");
            return false;
        }

        var modifiers = MacHotkeyModifiers.None;
        if (CommandCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Command;
        if (OptionCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Option;
        if (ControlCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Control;
        if (ShiftCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Shift;
        if ((HotkeyKeyComboBox.SelectedItem as ComboBoxItem)?.Tag is not MacVirtualKey key)
        {
            error = UiStrings.Get("mac.settings.hotkey_choose");
            return false;
        }

        if (!TryCreateQuerySourceSettings(
                includeCurrentUnsavedProvider: true,
                out var querySources,
                out error))
        {
            return false;
        }

        input = new MacSettingsInput(
            SelectedProviderId(),
            EndpointTextBox.Text ?? string.Empty,
            ModelTextBox.Text,
            SourceLanguageTextBox.Text,
            TargetLanguageTextBox.Text ?? string.Empty,
            checked((int)(TimeoutNumericUpDown.Value ?? 45)),
            CredentialTextBox.Text,
            SecondaryCredentialTextBox.Text,
            ClearCredentialCheckBox.IsChecked == true,
            querySources,
            new ProxySettings(ProxySettingsMigration.CurrentVersion, proxyMode, proxyUri),
            new MacHotkeySettings(MacHotkeySettingsMigration.CurrentVersion, modifiers, key),
            StartAtLoginCheckBox.IsChecked == true,
            new HistoryRetention(
                checked((int)(MaxEntriesNumericUpDown.Value ?? 100)),
                checked((int)(MaxAgeNumericUpDown.Value ?? 30))));
        return true;
    }

    private bool TryCreateQuerySourceSettings(
        bool includeCurrentUnsavedProvider,
        out QuerySourceSettings settings,
        out string error)
    {
        settings = default!;
        error = string.Empty;
        var selectedProviderId = SelectedProviderId();
        var providers = new List<ProviderDescriptor>();
        foreach (var checkBox in SourceCheckBoxes())
        {
            if (checkBox.IsChecked != true || checkBox.Tag is not string providerId)
            {
                continue;
            }

            if (includeCurrentUnsavedProvider &&
                string.Equals(providerId, selectedProviderId, StringComparison.Ordinal))
            {
                providers.Add(new ProviderDescriptor(providerId));
                continue;
            }

            if (!_profiles.TryGetValue(providerId, out var profile))
            {
                error = UiStrings.Get("mac.settings.configure_each");
                return false;
            }

            providers.Add(profile.Provider);
        }

        var localDictionaryEnabled = LocalDictionaryEnabledCheckBox.IsChecked == true;
        var localDictionaryPath = string.IsNullOrWhiteSpace(LocalDictionaryPathTextBox.Text)
            ? null
            : LocalDictionaryPathTextBox.Text.Trim();
        if (localDictionaryEnabled &&
            (localDictionaryPath is null || !Path.IsPathFullyQualified(localDictionaryPath) || !File.Exists(localDictionaryPath)))
        {
            error = UiStrings.Get("mac.settings.dictionary_file");
            return false;
        }

        var candidate = new QuerySourceSettings(
            QuerySourceSettingsMigration.CurrentVersion,
            providers,
            new LocalDictionarySettings(localDictionaryEnabled, localDictionaryPath),
            MacSystemDictionaryCheckBox.IsChecked == true);
        try
        {
            candidate.Validate();
            settings = candidate;
            return true;
        }
        catch (ContractValidationException)
        {
            error = UiStrings.Get("mac.settings.enable_source");
            return false;
        }
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
        MacSystemDictionaryCheckBox.IsChecked = settings.MacSystemDictionaryEnabled;
    }

    private void ApplyHotkey(MacHotkeySettings settings)
    {
        CommandCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Command);
        OptionCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Option);
        ControlCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Control);
        ShiftCheckBox.IsChecked = settings.Modifiers.HasFlag(MacHotkeyModifiers.Shift);
        SelectByTag(HotkeyKeyComboBox, settings.Key, fallbackIndex: 3);
    }

    private string SelectedProviderId() => SelectedTag(ProviderComboBox) ??
        TranslationProviderIds.OpenAiCompatible;

    private static string? SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static void SelectByTag(ComboBox comboBox, object tag, int fallbackIndex)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (Equals(item.Tag, tag) ||
                string.Equals(item.Tag?.ToString(), tag.ToString(), StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedIndex = fallbackIndex;
    }

    private static string DescribeKey(MacVirtualKey key) => key.ToString() switch
    {
        var value when value.StartsWith("Digit", StringComparison.Ordinal) => value[5..],
        var value => value,
    };

    private IReadOnlyList<CheckBox> SourceCheckBoxes() =>
    [
        OpenAiSourceCheckBox,
        DeepLSourceCheckBox,
        OllamaSourceCheckBox,
        BingSourceCheckBox,
        GoogleSourceCheckBox,
        VolcengineSourceCheckBox,
    ];

    private void HandleClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (!_allowClose)
        {
            eventArgs.Cancel = true;
            ClearProviderDrafts();
            Hide();
        }
    }

    internal void PrepareForShutdown()
    {
        ClearProviderDrafts();
        _allowClose = true;
    }
}
