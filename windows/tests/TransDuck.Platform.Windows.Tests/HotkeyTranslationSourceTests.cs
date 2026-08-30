// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class HotkeyTranslationSourceTests
{
    [Fact]
    public void SuccessfulSelection_ContinuesIntoTranslationWithinTheSameOperation()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");
        var selection = Slice(
            source,
            "private async Task ReadSelectionAsync()",
            "private Task TranslateAsync(");
        var success = selection.IndexOf(
            "selection.Status == SelectionReadStatus.Succeeded",
            StringComparison.Ordinal);
        var present = selection.IndexOf("_resultWindow.Present(text)", success, StringComparison.Ordinal);
        var translate = selection.IndexOf(
            "await TranslateAsync(text, operation, cancellationToken)",
            success,
            StringComparison.Ordinal);

        Assert.True(success >= 0);
        Assert.True(present > success);
        Assert.True(translate > present);
    }

    [Fact]
    public void ManualTranslation_StartsAnOperationBeforeDelegatingToTheSharedTranslationPath()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");
        var wrapper = Slice(
            source,
            "private Task TranslateAsync(",
            "private async Task TranslateAsync(");
        var begin = wrapper.IndexOf("BeginOperation()", StringComparison.Ordinal);
        var translate = wrapper.IndexOf(
            "return TranslateAsync(text, operation, cancellationToken, sourceFilter)",
            StringComparison.Ordinal);

        Assert.True(begin >= 0);
        Assert.True(translate > begin);
    }

    [Fact]
    public void Translation_FansOutEnabledProvidersAndDictionariesBeforeJoiningAllResults()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");
        var translate = Slice(
            source,
            "private async Task TranslateAsync(",
            "private async Task<QuerySourceTerminal> RunTranslationSourceAsync");

        Assert.Contains("selectedSources.EnabledTranslationProviders", translate,
            StringComparison.Ordinal);
        Assert.Contains("RunTranslationSourceAsync", translate, StringComparison.Ordinal);
        Assert.Contains("RunDictionarySourceAsync", translate, StringComparison.Ordinal);
        Assert.Contains("await Task.WhenAll(runs)", translate, StringComparison.Ordinal);
        Assert.Contains("_resultWindow.BeginResults(", translate, StringComparison.Ordinal);
        Assert.Contains("presentations,", translate, StringComparison.Ordinal);
    }

    [Fact]
    public void Retry_PreservesSuccessfulCardsAndOnlyRunsRetryableSources()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");

        Assert.Contains("TranslateAsync(snapshot.SourceText, snapshot.SourceKeys)", source,
            StringComparison.Ordinal);
        Assert.Contains("sourceFilter.Contains(CanonicalProviderKey(provider))", source,
            StringComparison.Ordinal);
        Assert.Contains("preserveExisting: sourceFilter is not null", source,
            StringComparison.Ordinal);
        Assert.Contains("_resultWindow.MarkActiveSourcesCancelled()", source,
            StringComparison.Ordinal);
        Assert.Contains("TranslationStreamEventKind.Completed => string.Empty", source,
            StringComparison.Ordinal);
        Assert.Contains("DictionaryLookupStatus.Found => string.Empty", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("translation.status.completed", source, StringComparison.Ordinal);
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
