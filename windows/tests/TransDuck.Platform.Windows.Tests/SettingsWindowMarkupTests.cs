// Copyright (c) 2026 maywine. All rights reserved.

using System.Xml.Linq;

namespace TransDuck.Platform.Windows.Tests;

public sealed class SettingsWindowMarkupTests
{
    [Fact]
    public void SettingsWindow_DeclaresUniqueAutomationIdsForRequiredSettingsControls()
    {
        var document = XDocument.Load(FindSettingsWindowPath());
        var controls = document.Descendants()
            .Select(element => new
            {
                Element = element,
                AutomationId = element.Attributes().SingleOrDefault(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.AutomationId")?.Value,
            })
            .Where(control => control.AutomationId is not null)
            .ToArray();
        var automationIds = controls.Select(control => control.AutomationId!).ToArray();
        var required = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ProviderComboBox"] = "ComboBox",
            ["OpenAiSourceCheckBox"] = "CheckBox",
            ["DeepLSourceCheckBox"] = "CheckBox",
            ["OllamaSourceCheckBox"] = "CheckBox",
            ["BingSourceCheckBox"] = "CheckBox",
            ["GoogleSourceCheckBox"] = "CheckBox",
            ["VolcengineSourceCheckBox"] = "CheckBox",
            ["LocalDictionaryEnabledCheckBox"] = "CheckBox",
            ["LocalDictionaryPathTextBox"] = "TextBox",
            ["BrowseLocalDictionaryButton"] = "Button",
            ["SaveQuerySourcesButton"] = "Button",
            ["InstanceIdTextBox"] = "TextBox",
            ["EndpointTextBox"] = "TextBox",
            ["ModelTextBox"] = "TextBox",
            ["SourceLanguageTextBox"] = "TextBox",
            ["TargetLanguageTextBox"] = "TextBox",
            ["TimeoutSecondsTextBox"] = "TextBox",
            ["HistoryMaxEntriesTextBox"] = "TextBox",
            ["HistoryMaxAgeDaysTextBox"] = "TextBox",
            ["CredentialPasswordBox"] = "TextBox",
            ["VolcengineAccessKeyIdPasswordBox"] = "TextBox",
            ["ProxyModeComboBox"] = "ComboBox",
            ["CustomHttpProxyUriTextBox"] = "TextBox",
            ["ProxyStatusTextBlock"] = "TextBlock",
            ["SaveProxySettingsButton"] = "Button",
            ["ControlHotkeyModifierCheckBox"] = "CheckBox",
            ["AltHotkeyModifierCheckBox"] = "CheckBox",
            ["ShiftHotkeyModifierCheckBox"] = "CheckBox",
            ["WindowsHotkeyModifierCheckBox"] = "CheckBox",
            ["HotkeyKeyTextBox"] = "TextBox",
            ["HotkeyStatusTextBlock"] = "TextBlock",
            ["SaveHotkeyButton"] = "Button",
            ["LaunchAtStartupCheckBox"] = "CheckBox",
            ["StartupStatusTextBlock"] = "TextBlock",
            ["SaveStartupButton"] = "Button",
            ["SaveProviderSettingsButton"] = "Button",
            ["ClearCredentialButton"] = "Button",
            ["CredentialStatusTextBlock"] = "TextBlock",
            ["SettingsStatusTextBlock"] = "TextBlock",
            ["CloseSettingsButton"] = "Button",
        };

        Assert.Equal(automationIds.Length, automationIds.Distinct(StringComparer.Ordinal).Count());
        foreach (var requiredControl in required)
        {
            var control = Assert.Single(controls, candidate => string.Equals(
                candidate.AutomationId,
                requiredControl.Key,
                StringComparison.Ordinal));
            Assert.Equal(requiredControl.Value, control.Element.Name.LocalName);
        }

        Assert.Equal("TransDuck.UI.Views.SettingsWindowBase", document.Root!.Attributes().Single(attribute =>
            attribute.Name.LocalName == "Class").Value);
        var version = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "ProductVersionTextBlock", StringComparison.Ordinal)).Element;
        Assert.Equal("TextBlock", version.Name.LocalName);
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", version.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
        Assert.Equal("{DynamicResource settings.automation.product_version.help}", version.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.HelpText")?.Value);
        var credentialStatus = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "CredentialStatusTextBlock", StringComparison.Ordinal)).Element;
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", credentialStatus.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
        Assert.Equal("{DynamicResource settings.automation.credential_status.help}", credentialStatus.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.HelpText")?.Value);
        var hotkeyKeyTextBox = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "HotkeyKeyTextBox", StringComparison.Ordinal)).Element;
        Assert.Equal("3", hotkeyKeyTextBox.Attribute("MaxLength")?.Value);
        var saveHotkeyButton = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "SaveHotkeyButton", StringComparison.Ordinal)).Element;
        Assert.Equal("HandleSaveHotkeyClick", saveHotkeyButton.Attribute("Click")?.Value);
        var proxyMode = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "ProxyModeComboBox", StringComparison.Ordinal)).Element;
        Assert.Equal(
            new[] { "SystemDefault", "CustomHttp", "Disabled" },
            proxyMode.Elements()
                .Where(element => element.Name.LocalName == "ComboBoxItem")
                .Select(element => element.Attribute("Tag")?.Value));
        var proxyStatus = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "ProxyStatusTextBlock", StringComparison.Ordinal)).Element;
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", proxyStatus.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
        Assert.Equal("{DynamicResource settings.automation.proxy_status.help}", proxyStatus.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.HelpText")?.Value);
        var saveProxyButton = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "SaveProxySettingsButton", StringComparison.Ordinal)).Element;
        Assert.Equal("HandleSaveProxyClick", saveProxyButton.Attribute("Click")?.Value);
        var startupStatus = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "StartupStatusTextBlock", StringComparison.Ordinal)).Element;
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", startupStatus.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
        Assert.Equal("{DynamicResource settings.automation.startup_status.help}", startupStatus.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.HelpText")?.Value);
        var saveStartupButton = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "SaveStartupButton", StringComparison.Ordinal)).Element;
        Assert.Equal("HandleSaveStartupClick", saveStartupButton.Attribute("Click")?.Value);
        var saveSourcesButton = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "SaveQuerySourcesButton", StringComparison.Ordinal)).Element;
        Assert.Equal("HandleSaveQuerySourcesClick", saveSourcesButton.Attribute("Click")?.Value);
        var providerComboBox = Assert.Single(controls, control =>
            string.Equals(control.AutomationId, "ProviderComboBox", StringComparison.Ordinal)).Element;
        Assert.Equal(
            new[] { "openai-compatible", "deepl", "ollama", "bing", "google", "volcengine" },
            providerComboBox.Elements()
                .Where(element => element.Name.LocalName == "ComboBoxItem")
                .Select(element => element.Attribute("Tag")?.Value));
        var bingProvider = Assert.Single(providerComboBox.Elements(), element =>
            string.Equals(element.Attribute("Tag")?.Value, "bing", StringComparison.Ordinal));
        var googleProvider = Assert.Single(providerComboBox.Elements(), element =>
            string.Equals(element.Attribute("Tag")?.Value, "google", StringComparison.Ordinal));
        var volcengineProvider = Assert.Single(providerComboBox.Elements(), element =>
            string.Equals(element.Attribute("Tag")?.Value, "volcengine", StringComparison.Ordinal));
        Assert.Equal("{DynamicResource provider.name.bing}", bingProvider.Attribute("Content")?.Value);
        Assert.Equal("{DynamicResource provider.name.google}", googleProvider.Attribute("Content")?.Value);
        Assert.Equal("{DynamicResource provider.name.volcengine}", volcengineProvider.Attribute("Content")?.Value);
        var sourceIds = new[]
        {
            "openai-compatible", "deepl", "ollama", "bing", "google", "volcengine",
        };
        Assert.Equal(sourceIds, new[]
        {
            "OpenAiSourceCheckBox", "DeepLSourceCheckBox", "OllamaSourceCheckBox",
            "BingSourceCheckBox", "GoogleSourceCheckBox", "VolcengineSourceCheckBox",
        }.Select(id => Assert.Single(controls, control =>
            string.Equals(control.AutomationId, id, StringComparison.Ordinal)).Element.Attribute("Tag")?.Value));
    }

    private static string FindSettingsWindowPath()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "ui",
                    "TransDuck.UI",
                    "Views",
                    "SettingsWindowBase.axaml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("SettingsWindowBase.axaml was not found from the test host path.");
    }
}
