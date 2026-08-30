// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class ResultFloatingWindowSourceTests
{
    [Fact]
    public void Deactivation_HidesVisibleWindowWithoutInterferingWithFinalClose()
    {
        var source = ReadSource("TransDuck.App", "Windows", "ResultFloatingWindow.xaml.cs");
        var deactivated = Slice(
            source,
            "protected override void OnDeactivated(EventArgs eventArgs)",
            "private void TranslateButtonClick");
        var baseCall = deactivated.IndexOf("base.OnDeactivated(eventArgs)", StringComparison.Ordinal);
        var finalCloseGuard = deactivated.IndexOf("!_allowClose", StringComparison.Ordinal);
        var visibleGuard = deactivated.IndexOf("IsVisible", finalCloseGuard, StringComparison.Ordinal);
        var hide = deactivated.IndexOf("Hide()", visibleGuard, StringComparison.Ordinal);

        Assert.True(baseCall >= 0);
        Assert.True(finalCloseGuard > baseCall);
        Assert.True(visibleGuard > finalCloseGuard);
        Assert.True(hide > visibleGuard);
    }

    [Fact]
    public void DictionaryResultCarriesTheEntryTermWithoutParsingRenderedText()
    {
        var runtime = ReadSource("TransDuck.App", "AppRuntime.cs");
        var window = ReadSource("TransDuck.App", "Windows", "ResultFloatingWindow.xaml.cs");

        Assert.Contains("result.Entry?.Term", runtime, StringComparison.Ordinal);
        Assert.Contains("PronunciationTerm", window, StringComparison.Ordinal);
        Assert.Contains("PronunciationRequested?.Invoke(this, term)", window, StringComparison.Ordinal);
        Assert.DoesNotContain("ToDisplayText().Split", runtime + window, StringComparison.Ordinal);
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
