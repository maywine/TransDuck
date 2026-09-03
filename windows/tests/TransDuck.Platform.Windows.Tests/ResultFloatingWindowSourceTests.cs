// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class ResultFloatingWindowSourceTests
{
    [Fact]
    public void Deactivation_HidesVisibleWindowWithoutInterferingWithFinalClose()
    {
        var source = ReadSource("TransDuck.App", "Windows", "ResultFloatingWindow.cs");
        var deactivated = Slice(
            source,
            "private void HandleDeactivated(object? sender, EventArgs eventArgs)",
            "private void HandleHideRequested");
        var finalCloseGuard = deactivated.IndexOf("!_allowClose", StringComparison.Ordinal);
        var visibleGuard = deactivated.IndexOf("IsVisible", finalCloseGuard, StringComparison.Ordinal);
        var hide = deactivated.IndexOf("Hide()", visibleGuard, StringComparison.Ordinal);

        Assert.True(finalCloseGuard >= 0);
        Assert.True(visibleGuard > finalCloseGuard);
        Assert.True(hide > visibleGuard);
    }

    [Fact]
    public void DictionaryResultCarriesTheEntryTermWithoutParsingRenderedText()
    {
        var runtime = ReadSource("TransDuck.App", "AppRuntime.cs");
        var window = ReadSource("TransDuck.App", "Windows", "ResultFloatingWindow.cs");
        var sharedUi = ReadRepositoryFile("ui", "TransDuck.UI", "PresentationModels.cs") +
            ReadRepositoryFile("ui", "TransDuck.UI", "Views", "TranslationWindowBase.axaml.cs");

        Assert.Contains("result.Entry?.Term", runtime, StringComparison.Ordinal);
        Assert.Contains("PronunciationTerm", sharedUi, StringComparison.Ordinal);
        Assert.Contains("PronunciationRequested?.Invoke(this, term)", sharedUi, StringComparison.Ordinal);
        Assert.DoesNotContain("ToDisplayText().Split", runtime + window + sharedUi, StringComparison.Ordinal);
    }

    [Fact]
    public void VersionText_UsesTheSharedAssemblyDisplayWithoutHardcoding()
    {
        var result = ReadSource("TransDuck.App", "Windows", "ResultFloatingWindow.cs");
        var settings = ReadSource("TransDuck.App", "Windows", "SettingsWindow.cs");
        const string versionDisplay = "TransDuck.Core.ProductVersionDisplay.FromAssembly(typeof(App).Assembly)";

        Assert.Contains(versionDisplay, result, StringComparison.Ordinal);
        Assert.Contains(versionDisplay, settings, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bv\d+\.\d+(?:\.\d+){0,2}\b", result + settings);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The required source method must be present.");
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, "The required source method must have a bounded body.");
        return source[start..end];
    }

    private static string ReadSource(string projectDirectory, params string[] relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    [directory.FullName, "windows", "src", projectDirectory, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return StripComments(File.ReadAllText(candidate));
                }
            }
        }

        throw new FileNotFoundException("The requested Windows source file was not found from the test host path.");
    }

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return StripComments(File.ReadAllText(candidate));
                }
            }
        }

        throw new FileNotFoundException("The requested repository source file was not found.");
    }

    private static string StripComments(string source)
    {
        source = Regex.Replace(source, "/\\*[\\s\\S]*?\\*/", string.Empty);
        return string.Join(
            Environment.NewLine,
            source.Split('\n').Select(line =>
            {
                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }));
    }
}
