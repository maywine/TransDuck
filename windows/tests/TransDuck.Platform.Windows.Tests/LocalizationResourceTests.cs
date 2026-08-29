// Copyright (c) 2026 maywine. All rights reserved.

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Platform.Windows.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly Regex ResourceKeyPattern = new(
        "^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex PlaceholderIndexPattern = new(
        "(?<!\\{)\\{(?<index>\\d+)(?:,-?\\d+)?(?::[^}]*)?\\}",
        RegexOptions.CultureInvariant);

    [Fact]
    public void ResourceDictionaries_HaveExactNonEmptyAndWellFormedKeySets()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var chinese = ReadResourceDictionary("Strings.zh-CN.xaml");

        Assert.NotEmpty(english);
        Assert.Equal(
            english.Keys.OrderBy(key => key, StringComparer.Ordinal),
            chinese.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.All(english.Concat(chinese), entry =>
        {
            Assert.Matches(ResourceKeyPattern, entry.Key);
            Assert.False(string.IsNullOrWhiteSpace(entry.Value));
        });
    }

    [Fact]
    public void TranslationErrorCodeResources_AreStableCamelCaseAcrossLocales()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var chinese = ReadResourceDictionary("Strings.zh-CN.xaml");
        var expected = Enum.GetValues<QueryErrorCode>()
            .Select(errorCode => new
            {
                Key = "translation.error_code." + ToSnakeCase(errorCode.ToString()),
                Value = ToLowerCamelCase(errorCode.ToString()),
            })
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        var actualKeys = english.Keys
            .Where(key => key.StartsWith("translation.error_code.", StringComparison.Ordinal))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected.Select(entry => entry.Key), actualKeys);
        foreach (var entry in expected)
        {
            Assert.Equal(entry.Value, english[entry.Key]);
            Assert.Equal(entry.Value, chinese[entry.Key]);
        }
    }

    [Fact]
    public void ResourceDictionaries_HaveCompatibleCompositeFormatPlaceholders()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var chinese = ReadResourceDictionary("Strings.zh-CN.xaml");

        foreach (var key in english.Keys)
        {
            var englishIndices = GetPlaceholderIndices(english[key]);
            var chineseIndices = GetPlaceholderIndices(chinese[key]);

            Assert.Equal(englishIndices, chineseIndices);
            AssertFormatSucceeds(english[key], englishIndices);
            AssertFormatSucceeds(chinese[key], chineseIndices);
        }
    }

    [Fact]
    public void AppAndPrimaryWindows_UseFallbackAndDeclaredResourceKeys()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var app = XDocument.Load(FindAppFile("App.xaml"));
        var result = XDocument.Load(FindAppFile("Windows", "ResultFloatingWindow.xaml"));
        var settings = XDocument.Load(FindAppFile("Windows", "SettingsWindow.xaml"));
        var history = XDocument.Load(FindAppFile("Windows", "HistoryWindow.xaml"));
        var fallback = Assert.Single(app.Descendants(), element =>
            element.Name.LocalName == "ResourceDictionary" &&
            string.Equals(element.Attribute("Source")?.Value, "Resources/Strings.en-US.xaml", StringComparison.Ordinal));

        Assert.Equal("Resources/Strings.en-US.xaml", fallback.Attribute("Source")?.Value);
        AssertResourceReference(result.Root!, "Title", "result.window.title");
        AssertResourceReference(FindAutomationElement(result, "RetryButton"), "Content", "result.button.retry");
        AssertResourceReference(settings.Root!, "Title", "settings.window.title");
        AssertResourceReference(FindAutomationElement(settings, "SaveHotkeyButton"), "Content", "settings.button.save_hotkey");
        AssertResourceReference(FindAutomationElement(settings, "LaunchAtStartupCheckBox"), "Content", "settings.startup.enabled");
        AssertResourceReference(FindAutomationElement(settings, "LaunchAtStartupCheckBox"), "AutomationProperties.Name", "settings.automation.startup_enabled.name");
        AssertResourceReference(FindAutomationElement(settings, "LaunchAtStartupCheckBox"), "AutomationProperties.HelpText", "settings.automation.startup_enabled.help");
        AssertResourceReference(FindAutomationElement(settings, "StartupStatusTextBlock"), "AutomationProperties.HelpText", "settings.automation.startup_status.help");
        AssertResourceReference(FindAutomationElement(settings, "SaveStartupButton"), "Content", "settings.button.save_startup");
        AssertResourceReference(history.Root!, "Title", "history.window.title");
        AssertResourceReference(FindAutomationElement(history, "RefreshHistoryButton"), "Content", "history.button.refresh");

        foreach (var document in new[] { result, settings, history })
        {
            var referencedKeys = document.Root!.DescendantsAndSelf()
                .Attributes()
                .Select(attribute => TryGetResourceKey(attribute.Value, out var key) ? key : null)
                .Where(key => key is not null)
                .Cast<string>()
                .ToArray();

            Assert.NotEmpty(referencedKeys);
            Assert.All(referencedKeys, key => Assert.True(english.ContainsKey(key)));
        }
    }

    [Fact]
    public void EditableControls_HaveNonEmptyDynamicResourceAutomationNames()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var settings = XDocument.Load(FindAppFile("Windows", "SettingsWindow.xaml"));
        var result = XDocument.Load(FindAppFile("Windows", "ResultFloatingWindow.xaml"));
        var settingsEditableControls = new[]
        {
            "ProviderComboBox",
            "InstanceIdTextBox",
            "EndpointTextBox",
            "ModelTextBox",
            "SourceLanguageTextBox",
            "TargetLanguageTextBox",
            "TimeoutSecondsTextBox",
            "HistoryMaxEntriesTextBox",
            "HistoryMaxAgeDaysTextBox",
            "CredentialPasswordBox",
            "ProxyModeComboBox",
            "CustomHttpProxyUriTextBox",
            "ControlHotkeyModifierCheckBox",
            "AltHotkeyModifierCheckBox",
            "ShiftHotkeyModifierCheckBox",
            "WindowsHotkeyModifierCheckBox",
            "HotkeyKeyTextBox",
            "LaunchAtStartupCheckBox",
        };

        foreach (var automationId in settingsEditableControls)
        {
            AssertDynamicResourceAutomationName(FindAutomationElement(settings, automationId), english);
        }

        AssertDynamicResourceAutomationName(FindAutomationElement(result, "OcrLanguageBox"), english);
    }

    [Fact]
    public void StartupResources_AreDeclaredInBothLocalesWithoutLockingNaturalLanguage()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var chinese = ReadResourceDictionary("Strings.zh-CN.xaml");
        var expectedKeys = new[]
        {
            "settings.heading.startup",
            "settings.description.startup",
            "settings.startup.enabled",
            "settings.button.save_startup",
            "settings.automation.startup_enabled.name",
            "settings.automation.startup_enabled.help",
            "settings.automation.startup_status.help",
            "startup.status.loading",
            "startup.status.enabled",
            "startup.status.disabled",
            "startup.status.stale",
            "startup.status.conflict",
            "startup.status.unavailable",
            "startup.status.failed",
        };

        foreach (var key in expectedKeys)
        {
            Assert.True(english.TryGetValue(key, out var englishValue));
            Assert.True(chinese.TryGetValue(key, out var chineseValue));
            Assert.False(string.IsNullOrWhiteSpace(englishValue));
            Assert.False(string.IsNullOrWhiteSpace(chineseValue));
        }
    }

    [Fact]
    public void WebProviderResources_AreDeclaredInBothLocalesWithoutLockingNaturalLanguage()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var chinese = ReadResourceDictionary("Strings.zh-CN.xaml");
        var expectedKeys = new[]
        {
            "provider.name.bing",
            "provider.name.google",
            "provider.status.credential_not_required",
        };

        foreach (var key in expectedKeys)
        {
            Assert.True(english.TryGetValue(key, out var englishValue));
            Assert.True(chinese.TryGetValue(key, out var chineseValue));
            Assert.False(string.IsNullOrWhiteSpace(englishValue));
            Assert.False(string.IsNullOrWhiteSpace(chineseValue));
        }
    }

    [Fact]
    public void ProxyResources_AreDeclaredInBothLocalesWithoutLockingNaturalLanguage()
    {
        var english = ReadResourceDictionary("Strings.en-US.xaml");
        var chinese = ReadResourceDictionary("Strings.zh-CN.xaml");
        var expectedKeys = new[]
        {
            "settings.heading.proxy",
            "settings.description.proxy",
            "settings.label.proxy_mode",
            "settings.proxy.mode.system_default",
            "settings.proxy.mode.custom_http",
            "settings.proxy.mode.disabled",
            "settings.label.custom_http_proxy_uri",
            "settings.button.save_proxy",
            "settings.automation.proxy_mode.name",
            "settings.automation.proxy_mode.help",
            "settings.automation.custom_http_proxy_uri.name",
            "settings.automation.custom_http_proxy_uri.help",
            "settings.automation.proxy_status.help",
            "proxy.status.loading",
            "proxy.status.failed",
            "proxy.save.invalid",
            "proxy.ui.invalid",
        };

        foreach (var key in expectedKeys)
        {
            Assert.True(english.TryGetValue(key, out var englishValue));
            Assert.True(chinese.TryGetValue(key, out var chineseValue));
            Assert.False(string.IsNullOrWhiteSpace(englishValue));
            Assert.False(string.IsNullOrWhiteSpace(chineseValue));
        }
    }

    private static IReadOnlyDictionary<string, string> ReadResourceDictionary(string fileName)
    {
        var document = XDocument.Load(FindAppFile("Resources", fileName));
        var entries = document.Root!
            .Elements()
            .Select(element => new ResourceEntry(
                element.Attributes().SingleOrDefault(attribute => attribute.Name.LocalName == "Key")?.Value,
                element.Value.Trim()))
            .Where(entry => entry.Key is not null)
            .Select(entry => new ResourceEntry(entry.Key!, entry.Value))
            .ToArray();
        var duplicates = entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
        return entries.ToDictionary(entry => entry.Key!, entry => entry.Value, StringComparer.Ordinal);
    }

    private static void AssertResourceReference(XElement element, string attributeName, string expectedKey) =>
        Assert.Equal($"{{DynamicResource {expectedKey}}}", element.Attribute(attributeName)?.Value);

    private static XElement FindAutomationElement(XDocument document, string automationId) =>
        Assert.Single(document.Descendants(), element => string.Equals(
            element.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId")?.Value,
            automationId,
            StringComparison.Ordinal));

    private static bool TryGetResourceKey(string value, out string key)
    {
        const string dynamicPrefix = "{DynamicResource ";
        const string staticPrefix = "{StaticResource ";
        if ((value.StartsWith(dynamicPrefix, StringComparison.Ordinal) ||
             value.StartsWith(staticPrefix, StringComparison.Ordinal)) &&
            value.EndsWith('}'))
        {
            var prefixLength = value.StartsWith(dynamicPrefix, StringComparison.Ordinal)
                ? dynamicPrefix.Length
                : staticPrefix.Length;
            key = value[prefixLength..^1];
            return !string.IsNullOrWhiteSpace(key);
        }

        key = string.Empty;
        return false;
    }

    private static void AssertDynamicResourceAutomationName(
        XElement element,
        IReadOnlyDictionary<string, string> english)
    {
        var resourceReference = element.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == "AutomationProperties.Name")?.Value;

        Assert.NotNull(resourceReference);
        Assert.True(TryGetDynamicResourceKey(resourceReference!, out var key));
        Assert.True(english.TryGetValue(key, out var value));
        Assert.False(string.IsNullOrWhiteSpace(value));
    }

    private static bool TryGetDynamicResourceKey(string value, out string key)
    {
        const string prefix = "{DynamicResource ";
        if (value.StartsWith(prefix, StringComparison.Ordinal) && value.EndsWith('}'))
        {
            key = value[prefix.Length..^1];
            return !string.IsNullOrWhiteSpace(key);
        }

        key = string.Empty;
        return false;
    }

    private static string FindAppFile(params string[] relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    [directory.FullName, "windows", "src", "TransDuck.App", .. relativePath]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("The TransDuck.App source file was not found from the test host path.");
    }

    private static string ToLowerCamelCase(string value) =>
        char.ToLowerInvariant(value[0]) + value[1..];

    private static string ToSnakeCase(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character)
            ? "_" + char.ToLowerInvariant(character)
            : char.ToLowerInvariant(character).ToString()));

    private static int[] GetPlaceholderIndices(string value) => PlaceholderIndexPattern
        .Matches(value)
        .Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture))
        .Distinct()
        .OrderBy(index => index)
        .ToArray();

    private static void AssertFormatSucceeds(string value, IReadOnlyList<int> indices)
    {
        var argumentCount = indices.Count == 0 ? 0 : indices[^1] + 1;
        var arguments = Enumerable.Range(0, argumentCount)
            .Select(_ => (object)new FormatProbe())
            .ToArray();

        Assert.Null(Record.Exception(() => string.Format(CultureInfo.InvariantCulture, value, arguments)));
    }

    private sealed record ResourceEntry(string? Key, string Value);

    private sealed class FormatProbe : IFormattable
    {
        public override string ToString() => "probe";

        public string ToString(string? format, IFormatProvider? formatProvider) => "probe";
    }
}
