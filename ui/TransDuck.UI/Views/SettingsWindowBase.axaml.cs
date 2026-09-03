using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TransDuck.UI.Views;

public partial class SettingsWindowBase : Window
{
    public SettingsWindowBase()
    {
        InitializeComponent();
    }

    public event EventHandler<SelectionChangedEventArgs>? ProviderSelectionRequested;
    public event EventHandler? BrowseLocalDictionaryRequested;
    public event EventHandler? SaveQuerySourcesRequested;
    public event EventHandler<SelectionChangedEventArgs>? ProxyModeSelectionRequested;
    public event EventHandler? SaveProxyRequested;
    public event EventHandler? SaveHotkeyRequested;
    public event EventHandler? AccessibilityRequested;
    public event EventHandler? SaveStartupRequested;
    public event EventHandler? ReloadRequested;
    public event EventHandler? SaveAllRequested;
    public event EventHandler? SaveProviderRequested;
    public event EventHandler? ClearCredentialRequested;
    public event EventHandler? CloseRequested;

    protected TextBlock ProductVersionTextBlock => ProductVersionTextBlockElement;
    protected TextBlock VersionTextBlock => ProductVersionTextBlockElement;
    protected CheckBox OpenAiSourceCheckBox => OpenAiSourceCheckBoxElement;
    protected CheckBox DeepLSourceCheckBox => DeepLSourceCheckBoxElement;
    protected CheckBox OllamaSourceCheckBox => OllamaSourceCheckBoxElement;
    protected CheckBox BingSourceCheckBox => BingSourceCheckBoxElement;
    protected CheckBox GoogleSourceCheckBox => GoogleSourceCheckBoxElement;
    protected CheckBox VolcengineSourceCheckBox => VolcengineSourceCheckBoxElement;
    protected CheckBox LocalDictionaryEnabledCheckBox => LocalDictionaryEnabledCheckBoxElement;
    protected TextBox LocalDictionaryPathTextBox => LocalDictionaryPathTextBoxElement;
    protected Button BrowseLocalDictionaryButton => BrowseLocalDictionaryButtonElement;
    protected Button SaveQuerySourcesButton => SaveQuerySourcesButtonElement;
    protected CheckBox MacSystemDictionaryCheckBox => MacSystemDictionaryCheckBoxElement;
    protected ComboBox ProviderComboBox => ProviderComboBoxElement;
    protected TextBox InstanceIdTextBox => InstanceIdTextBoxElement;
    protected TextBox EndpointTextBox => EndpointTextBoxElement;
    protected TextBox ModelTextBox => ModelTextBoxElement;
    protected TextBox SourceLanguageTextBox => SourceLanguageTextBoxElement;
    protected TextBox TargetLanguageTextBox => TargetLanguageTextBoxElement;
    protected TextBox TimeoutSecondsTextBox => TimeoutSecondsTextBoxElement;
    protected TextBox HistoryMaxEntriesTextBox => HistoryMaxEntriesTextBoxElement;
    protected TextBox HistoryMaxAgeDaysTextBox => HistoryMaxAgeDaysTextBoxElement;
    protected NumericUpDown TimeoutNumericUpDown => TimeoutNumericUpDownElement;
    protected NumericUpDown MaxEntriesNumericUpDown => MaxEntriesNumericUpDownElement;
    protected NumericUpDown MaxAgeNumericUpDown => MaxAgeNumericUpDownElement;
    protected StackPanel VolcengineAccessKeyPanel => VolcengineAccessKeyPanelElement;
    protected TextBox VolcengineAccessKeyIdPasswordBox => VolcengineAccessKeyIdPasswordBoxElement;
    protected TextBlock CredentialLabelTextBlock => CredentialLabelTextBlockElement;
    protected TextBlock CredentialLabel => CredentialLabelTextBlockElement;
    protected TextBox CredentialPasswordBox => CredentialPasswordBoxElement;
    protected TextBox CredentialTextBox => CredentialPasswordBoxElement;
    protected TextBlock SecondaryCredentialLabel => SecondaryCredentialLabelElement;
    protected TextBox SecondaryCredentialTextBox => SecondaryCredentialTextBoxElement;
    protected CheckBox ClearCredentialCheckBox => ClearCredentialCheckBoxElement;
    protected TextBlock CredentialStatusTextBlock => CredentialStatusTextBlockElement;
    protected ComboBox ProxyModeComboBox => ProxyModeComboBoxElement;
    protected TextBox CustomHttpProxyUriTextBox => CustomHttpProxyUriTextBoxElement;
    protected TextBox ProxyUriTextBox => CustomHttpProxyUriTextBoxElement;
    protected TextBlock ProxyStatusTextBlock => ProxyStatusTextBlockElement;
    protected Button SaveProxySettingsButton => SaveProxySettingsButtonElement;
    protected CheckBox CommandCheckBox => CommandCheckBoxElement;
    protected CheckBox OptionCheckBox => OptionCheckBoxElement;
    protected CheckBox ControlHotkeyModifierCheckBox => ControlHotkeyModifierCheckBoxElement;
    protected CheckBox ControlCheckBox => ControlHotkeyModifierCheckBoxElement;
    protected CheckBox AltHotkeyModifierCheckBox => AltHotkeyModifierCheckBoxElement;
    protected CheckBox ShiftHotkeyModifierCheckBox => ShiftHotkeyModifierCheckBoxElement;
    protected CheckBox ShiftCheckBox => ShiftHotkeyModifierCheckBoxElement;
    protected CheckBox WindowsHotkeyModifierCheckBox => WindowsHotkeyModifierCheckBoxElement;
    protected TextBox HotkeyKeyTextBox => HotkeyKeyTextBoxElement;
    protected ComboBox HotkeyKeyComboBox => HotkeyKeyComboBoxElement;
    protected TextBlock HotkeyStatusTextBlock => HotkeyStatusTextBlockElement;
    protected Button SaveHotkeyButton => SaveHotkeyButtonElement;
    protected CheckBox LaunchAtStartupCheckBox => LaunchAtStartupCheckBoxElement;
    protected CheckBox StartAtLoginCheckBox => LaunchAtStartupCheckBoxElement;
    protected TextBlock StartupStatusTextBlock => StartupStatusTextBlockElement;
    protected Button SaveStartupButton => SaveStartupButtonElement;
    protected TextBlock SettingsStatusTextBlock => SettingsStatusTextBlockElement;
    protected TextBlock StatusTextBlock => SettingsStatusTextBlockElement;
    protected Button SaveProviderSettingsButton => SaveProviderSettingsButtonElement;
    protected Button ClearCredentialButton => ClearCredentialButtonElement;
    protected Button CloseSettingsButton => CloseSettingsButtonElement;
    protected Button SaveButton => SaveAllButtonElement;

    protected void ConfigureForWindowsSettingsWindow()
    {
        Width = 560;
        Height = 650;
        MinWidth = 520;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MacSystemDictionaryCheckBoxElement.IsVisible = false;
        InstanceIdPanelElement.IsVisible = true;
        WindowsRetentionPanelElement.IsVisible = true;
        MacRetentionPanelElement.IsVisible = false;
        ClearCredentialCheckBoxElement.IsVisible = false;
        CommandCheckBoxElement.IsVisible = false;
        OptionCheckBoxElement.IsVisible = false;
        ControlHotkeyModifierCheckBoxElement.IsVisible = true;
        AltHotkeyModifierCheckBoxElement.IsVisible = true;
        ShiftHotkeyModifierCheckBoxElement.IsVisible = true;
        WindowsHotkeyModifierCheckBoxElement.IsVisible = true;
        HotkeyKeyTextBoxElement.IsVisible = true;
        HotkeyKeyComboBoxElement.IsVisible = false;
        AccessibilityButtonElement.IsVisible = false;
        SaveProxySettingsButtonElement.IsVisible = true;
        SaveHotkeyButtonElement.IsVisible = true;
        SaveStartupButtonElement.IsVisible = true;
        ReloadButtonElement.IsVisible = false;
        SaveAllButtonElement.IsVisible = false;
        SaveProviderSettingsButtonElement.IsVisible = true;
        ClearCredentialButtonElement.IsVisible = true;
        CloseSettingsButtonElement.IsVisible = true;
    }

    protected void ConfigureForMacSettingsWindow()
    {
        Width = 700;
        Height = 820;
        MinWidth = 560;
        MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MacSystemDictionaryCheckBoxElement.IsVisible = true;
        InstanceIdPanelElement.IsVisible = false;
        WindowsRetentionPanelElement.IsVisible = false;
        MacRetentionPanelElement.IsVisible = true;
        VolcengineAccessKeyPanelElement.IsVisible = false;
        ClearCredentialCheckBoxElement.IsVisible = true;
        CommandCheckBoxElement.IsVisible = true;
        OptionCheckBoxElement.IsVisible = true;
        ControlHotkeyModifierCheckBoxElement.IsVisible = true;
        ControlHotkeyModifierCheckBoxElement.Content = UiStrings.Get("settings.hotkey.control_mac");
        AutomationProperties.SetName(
            ControlHotkeyModifierCheckBoxElement,
            UiStrings.Get("settings.hotkey.control_mac"));
        AltHotkeyModifierCheckBoxElement.IsVisible = false;
        ShiftHotkeyModifierCheckBoxElement.IsVisible = true;
        WindowsHotkeyModifierCheckBoxElement.IsVisible = false;
        HotkeyKeyTextBoxElement.IsVisible = false;
        HotkeyKeyComboBoxElement.IsVisible = true;
        AccessibilityButtonElement.IsVisible = true;
        SaveProxySettingsButtonElement.IsVisible = false;
        SaveHotkeyButtonElement.IsVisible = false;
        SaveStartupButtonElement.IsVisible = false;
        ReloadButtonElement.IsVisible = true;
        SaveAllButtonElement.IsVisible = true;
        SaveProviderSettingsButtonElement.IsVisible = false;
        ClearCredentialButtonElement.IsVisible = false;
        CloseSettingsButtonElement.IsVisible = false;
    }

    private void HandleProviderSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) =>
        ProviderSelectionRequested?.Invoke(this, eventArgs);

    private void HandleBrowseLocalDictionaryClick(object? sender, RoutedEventArgs eventArgs) =>
        BrowseLocalDictionaryRequested?.Invoke(this, EventArgs.Empty);

    private void HandleSaveQuerySourcesClick(object? sender, RoutedEventArgs eventArgs) =>
        SaveQuerySourcesRequested?.Invoke(this, EventArgs.Empty);

    private void HandleProxyModeSelectionChanged(object? sender, SelectionChangedEventArgs eventArgs) =>
        ProxyModeSelectionRequested?.Invoke(this, eventArgs);

    private void HandleSaveProxyClick(object? sender, RoutedEventArgs eventArgs) =>
        SaveProxyRequested?.Invoke(this, EventArgs.Empty);

    private void HandleSaveHotkeyClick(object? sender, RoutedEventArgs eventArgs) =>
        SaveHotkeyRequested?.Invoke(this, EventArgs.Empty);

    private void HandleAccessibilityClick(object? sender, RoutedEventArgs eventArgs) =>
        AccessibilityRequested?.Invoke(this, EventArgs.Empty);

    private void HandleSaveStartupClick(object? sender, RoutedEventArgs eventArgs) =>
        SaveStartupRequested?.Invoke(this, EventArgs.Empty);

    private void HandleReloadClick(object? sender, RoutedEventArgs eventArgs) =>
        ReloadRequested?.Invoke(this, EventArgs.Empty);

    private void HandleSaveAllClick(object? sender, RoutedEventArgs eventArgs) =>
        SaveAllRequested?.Invoke(this, EventArgs.Empty);

    private void HandleSaveProviderClick(object? sender, RoutedEventArgs eventArgs) =>
        SaveProviderRequested?.Invoke(this, EventArgs.Empty);

    private void HandleClearCredentialClick(object? sender, RoutedEventArgs eventArgs) =>
        ClearCredentialRequested?.Invoke(this, EventArgs.Empty);

    private void HandleCloseClick(object? sender, RoutedEventArgs eventArgs) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
