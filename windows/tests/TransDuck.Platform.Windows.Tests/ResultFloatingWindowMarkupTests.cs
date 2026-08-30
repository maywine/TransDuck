// Copyright (c) 2026 maywine. All rights reserved.

using System.Xml.Linq;

namespace TransDuck.Platform.Windows.Tests;

public sealed class ResultFloatingWindowMarkupTests
{
    [Fact]
    public void ResultFloatingWindow_DeclaresCopyResultAutomationIdAndHandler()
    {
        var document = XDocument.Load(FindResultFloatingWindowPath());
        var copyButton = FindAutomationElement(document, "CopyResultButton");

        Assert.Equal("TransDuck.App.Windows.ResultFloatingWindow", document.Root!.Attributes().Single(attribute =>
            attribute.Name.LocalName == "Class").Value);
        Assert.Equal("Button", copyButton.Name.LocalName);
        Assert.Equal("CopyResultButtonClick", copyButton.Attribute("Click")?.Value);
    }

    [Fact]
    public void ResultFloatingWindow_DeclaresResourceBackedHideButtonName()
    {
        var document = XDocument.Load(FindResultFloatingWindowPath());
        var hideButton = FindAutomationElement(document, "HideButton");

        Assert.Equal("Button", hideButton.Name.LocalName);
        Assert.Equal("{DynamicResource result.button.hide}", hideButton.Attributes()
            .SingleOrDefault(attribute => attribute.Name.LocalName == "AutomationProperties.Name")?.Value);
    }

    [Fact]
    public void ResultFloatingWindow_DeclaresDisabledRetryAndCollapsedErrorCodeControls()
    {
        var document = XDocument.Load(FindResultFloatingWindowPath());
        var retryButton = FindAutomationElement(document, "RetryButton");
        var errorCode = FindAutomationElement(document, "TranslationErrorCodeTextBlock");
        var status = FindAutomationElement(document, "StatusTextBlock");

        Assert.Equal("Button", retryButton.Name.LocalName);
        Assert.Equal("False", retryButton.Attribute("IsEnabled")?.Value);
        Assert.Equal("RetryButtonClick", retryButton.Attribute("Click")?.Value);
        Assert.Equal("TextBlock", errorCode.Name.LocalName);
        Assert.Equal("Collapsed", errorCode.Attribute("Visibility")?.Value);
        var errorCodeName = errorCode.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == "AutomationProperties.Name")?.Value;
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", errorCodeName);
        Assert.Equal("TextBlock", status.Name.LocalName);
        var statusName = status.Attributes().SingleOrDefault(attribute =>
            attribute.Name.LocalName == "AutomationProperties.Name")?.Value;
        Assert.Equal("{Binding Text, RelativeSource={RelativeSource Self}}", statusName);
    }

    [Fact]
    public void ResultFloatingWindow_UsesASeparateCardForEachQuerySource()
    {
        var document = XDocument.Load(FindResultFloatingWindowPath());
        var results = FindAutomationElement(document, "ResultItemsControl");

        Assert.Equal("ItemsControl", results.Name.LocalName);
        Assert.Contains(results.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            string.Equals(element.Attribute("Text")?.Value, "{Binding DisplayName}", StringComparison.Ordinal));
        Assert.Contains(results.Descendants(), element =>
            element.Name.LocalName == "TextBlock" &&
            string.Equals(element.Attribute("Text")?.Value, "{Binding Status}", StringComparison.Ordinal));
    }

    [Fact]
    public void ResultFloatingWindow_ShowsPronunciationOnlyForDictionaryEntries()
    {
        var document = XDocument.Load(FindResultFloatingWindowPath());
        var pronounceButton = FindAutomationElement(document, "PronounceButton");

        Assert.Equal("Button", pronounceButton.Name.LocalName);
        Assert.Equal("PronounceButtonClick", pronounceButton.Attribute("Click")?.Value);
        Assert.Equal("{Binding PronunciationTerm}", pronounceButton.Attribute("Tag")?.Value);
        Assert.Equal("{Binding PronunciationVisibility}", pronounceButton.Attribute("Visibility")?.Value);
        Assert.Equal("{DynamicResource result.button.pronounce}", pronounceButton.Attribute("Content")?.Value);
    }

    private static string FindResultFloatingWindowPath()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "windows",
                    "src",
                    "TransDuck.App",
                    "Windows",
                    "ResultFloatingWindow.xaml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("ResultFloatingWindow.xaml was not found from the test host path.");
    }

    private static XElement FindAutomationElement(XDocument document, string automationId) =>
        Assert.Single(document.Descendants(), element => string.Equals(
            element.Attributes().SingleOrDefault(attribute =>
                attribute.Name.LocalName == "AutomationProperties.AutomationId")?.Value,
            automationId,
            StringComparison.Ordinal));
}
