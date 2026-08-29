// Copyright (c) 2026 maywine. All rights reserved.

using System.Xml.Linq;

namespace TransDuck.Platform.Windows.Tests;

public sealed class HistoryWindowMarkupTests
{
    [Fact]
    public void HistoryWindow_DeclaresStableAccessibleControlsAndCommands()
    {
        var document = XDocument.Load(FindHistoryWindowPath());
        var controls = document.Descendants()
            .Select(element => new AutomationControl(
                element,
                element.Attributes().SingleOrDefault(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.AutomationId")?.Value))
            .Where(control => control.AutomationId is not null)
            .ToArray();
        var automationIds = controls.Select(control => control.AutomationId!).ToArray();
        var requiredControls = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HistoryListBox"] = "ListBox",
            ["HistorySourceTextBox"] = "TextBox",
            ["HistoryResultTextBox"] = "TextBox",
            ["HistoryStatusTextBlock"] = "TextBlock",
            ["RefreshHistoryButton"] = "Button",
            ["ClearHistoryButton"] = "Button",
            ["CloseHistoryButton"] = "Button",
        };
        var requiredHandlers = new Dictionary<string, (string AttributeName, string Handler)>(StringComparer.Ordinal)
        {
            ["HistoryListBox"] = ("SelectionChanged", "HistorySelectionChanged"),
            ["RefreshHistoryButton"] = ("Click", "RefreshHistoryButtonClick"),
            ["ClearHistoryButton"] = ("Click", "ClearHistoryButtonClick"),
            ["CloseHistoryButton"] = ("Click", "CloseHistoryButtonClick"),
        };

        Assert.Equal(automationIds.Length, automationIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("TransDuck.App.Windows.HistoryWindow", document.Root!.Attributes().Single(attribute =>
            attribute.Name.LocalName == "Class").Value);
        foreach (var requiredControl in requiredControls)
        {
            var control = Assert.Single(controls, candidate => string.Equals(
                candidate.AutomationId,
                requiredControl.Key,
                StringComparison.Ordinal));
            Assert.Equal(requiredControl.Value, control.Element.Name.LocalName);
        }

        foreach (var requiredHandler in requiredHandlers)
        {
            var control = Assert.Single(controls, candidate => string.Equals(
                candidate.AutomationId,
                requiredHandler.Key,
                StringComparison.Ordinal));
            Assert.Equal(requiredHandler.Value.Handler, control.Element.Attribute(
                requiredHandler.Value.AttributeName)?.Value);
        }

        AssertReadOnlyTextBox(controls, "HistorySourceTextBox");
        AssertReadOnlyTextBox(controls, "HistoryResultTextBox");
    }

    private static void AssertReadOnlyTextBox(
        IReadOnlyList<AutomationControl> controls,
        string automationId)
    {
        var control = Assert.Single(controls, candidate =>
            string.Equals(candidate.AutomationId, automationId, StringComparison.Ordinal));

        Assert.Equal("TextBox", control.Element.Name.LocalName);
        Assert.Equal("True", control.Element.Attribute("IsReadOnly")?.Value);
    }

    private static string FindHistoryWindowPath()
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
                    "HistoryWindow.xaml");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("HistoryWindow.xaml was not found from the test host path.");
    }

    private sealed record AutomationControl(XElement Element, string? AutomationId);
}
