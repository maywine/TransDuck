using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Proxy;
using TransDuck.Platform.MacOS.Hotkeys;
using TransDuck.Platform.MacOS.Startup;

namespace TransDuck.MacOS.App.Views;

internal partial class SettingsWindow : Window
{
    private readonly MacAppRuntime _runtime;
    private readonly Dictionary<string, ProviderProfileSettings> _profiles = new(StringComparer.Ordinal);
    private bool _loading;
    private bool _allowClose;

    public SettingsWindow(MacAppRuntime runtime)
    {
        _runtime = runtime;
        InitializeComponent();
        foreach (var definition in MacAppRuntime.ProviderDefinitions)
        {
            ProviderComboBox.Items.Add(new ComboBoxItem
            {
                Content = definition.DisplayName,
                Tag = definition.ProviderId,
            });
        }

        foreach (var key in Enum.GetValues<MacVirtualKey>())
        {
            HotkeyKeyComboBox.Items.Add(new ComboBoxItem { Content = DescribeKey(key), Tag = key });
        }

        Opened += HandleOpened;
        Closing += HandleClosing;
    }

    private void HandleOpened(object? sender, EventArgs eventArgs) => _ = LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        SaveButton.IsEnabled = false;
        StatusTextBlock.Text = "Loading settings...";
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
            MaxEntriesNumericUpDown.Value = snapshot.Configuration.HistoryRetention.MaxEntries;
            MaxAgeNumericUpDown.Value = snapshot.Configuration.HistoryRetention.MaxAgeDays;
            StartAtLoginCheckBox.IsChecked = snapshot.StartupResult.IsEnabled;
            StatusTextBlock.Text = snapshot.StartupResult.Status == MacStartupStatus.Conflict
                ? "A conflicting login-start entry exists; it will not be overwritten."
                : "Settings loaded.";
            await RefreshCredentialStatusAsync();
        }
        finally
        {
            _loading = false;
            SaveButton.IsEnabled = true;
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
            ProviderCredentialKind.None => "Credential (not required)",
            ProviderCredentialKind.Optional when providerId == TranslationProviderIds.Bing =>
                "Optional Bing Cookie",
            ProviderCredentialKind.Optional => "Optional API Key",
            ProviderCredentialKind.VolcenginePair => "Volcengine AccessKey ID",
            _ => "API Key",
        };
        var credentialEnabled = definition.CredentialKind != ProviderCredentialKind.None;
        CredentialTextBox.IsEnabled = credentialEnabled;
        SecondaryCredentialTextBox.IsEnabled = credentialEnabled;
        ClearCredentialCheckBox.IsEnabled = credentialEnabled;
    }

    private async Task RefreshCredentialStatusAsync()
    {
        var definition = MacAppRuntime.ProviderDefinitions.First(candidate =>
            candidate.ProviderId == SelectedProviderId());
        if (definition.CredentialKind == ProviderCredentialKind.None)
        {
            CredentialStatusTextBlock.Text = "This provider does not use a credential.";
            return;
        }

        var status = await _runtime.GetCredentialStatusAsync(definition.ProviderId, CancellationToken.None);
        CredentialStatusTextBlock.Text = status switch
        {
            PersistenceStatus.Succeeded => "A credential is saved in macOS Keychain.",
            PersistenceStatus.NotFound => "No credential is saved.",
            _ => "The Keychain credential status is unavailable.",
        };
    }

    private void HandleProxyModeSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) =>
        ApplyProxyInputState();

    private void ApplyProxyInputState() => ProxyUriTextBox.IsEnabled =
        string.Equals(SelectedTag(ProxyModeComboBox), nameof(ProxyMode.CustomHttp), StringComparison.Ordinal);

    private async void HandleAccessibilityClick(object? sender, RoutedEventArgs eventArgs)
    {
        var ready = await _runtime.EnsureAccessibilityAndHotkeyAsync(prompt: true);
        StatusTextBlock.Text = ready
            ? "Accessibility permission and global hotkey are ready."
            : "Grant access in System Settings, then click this button again.";
    }

    private void HandleReloadClick(object? sender, RoutedEventArgs eventArgs) => _ = LoadAsync();

    private async void HandleBrowseLocalDictionaryClick(object? sender, RoutedEventArgs eventArgs)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Choose a supported local dictionary CSV or SQLite file",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Local dictionary data")
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
            StatusTextBlock.Text = "The local dictionary file could not be selected.";
        }
    }

    private async void HandleSaveQuerySourcesClick(object? sender, RoutedEventArgs eventArgs)
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

    private async void HandleSaveClick(object? sender, RoutedEventArgs eventArgs)
    {
        SaveButton.IsEnabled = false;
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
                await LoadAsync();
            }
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private bool TryCreateInput(out MacSettingsInput? input, out string error)
    {
        input = null;
        error = string.Empty;
        if (!Enum.TryParse<ProxyMode>(SelectedTag(ProxyModeComboBox), out var proxyMode))
        {
            error = "Choose a proxy mode.";
            return false;
        }

        Uri? proxyUri = null;
        if (proxyMode == ProxyMode.CustomHttp &&
            !Uri.TryCreate(ProxyUriTextBox.Text, UriKind.Absolute, out proxyUri))
        {
            error = "Enter a valid custom HTTP proxy URI.";
            return false;
        }

        var modifiers = MacHotkeyModifiers.None;
        if (CommandCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Command;
        if (OptionCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Option;
        if (ControlCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Control;
        if (ShiftCheckBox.IsChecked == true) modifiers |= MacHotkeyModifiers.Shift;
        if ((HotkeyKeyComboBox.SelectedItem as ComboBoxItem)?.Tag is not MacVirtualKey key)
        {
            error = "Choose a hotkey key.";
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
                error = "Configure and save each enabled translation provider first.";
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
            error = "Choose an existing local dictionary CSV or SQLite file.";
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
            error = "Enable at least one configured translation or dictionary source.";
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
            Hide();
        }
    }

    internal void PrepareForShutdown() => _allowClose = true;
}
