// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class ProxyIntegrationSourceTests
{
    [Fact]
    public void AppRuntime_UsesOneLeaseSourceForLegacySseAndAllFiveProvidersBeforeTrayOrHotkeyStartup()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");
        var constructor = Slice(source, "public AppRuntime()", "public Task StartAsync()");
        var start = Slice(source, "private async Task StartCoreAsync()", "public void Dispose()");

        Assert.Contains("new ProxyTranslationHttpClientLeaseSource(_proxyHttpClientPool)", constructor,
            StringComparison.Ordinal);
        Assert.Contains("new OpenAiCompatibleSseClient(_translationClientLeaseSource)", constructor,
            StringComparison.Ordinal);
        foreach (var provider in new[]
                 {
                     "OpenAiCompatibleProvider",
                     "DeepLProvider",
                     "OllamaProvider",
                     "BingWebProvider",
                     "GoogleWebProvider",
                 })
        {
            Assert.Contains("Register(new " + provider + "(_translationClientLeaseSource))", constructor,
                StringComparison.Ordinal);
        }

        var initialize = start.IndexOf("await _proxySettingsController.InitializeAsync", StringComparison.Ordinal);
        var tray = start.IndexOf("_trayService.Start()", StringComparison.Ordinal);
        var hotkey = start.IndexOf("await _hotkeySettingsController.InitializeAsync", StringComparison.Ordinal);
        Assert.True(initialize >= 0 && initialize < tray && tray < hotkey);
    }

    [Fact]
    public void ProxyController_PersistsBeforeUpdatingThePoolAndWritesOnlyClosedSafeDiagnostics()
    {
        var source = ReadSource("TransDuck.App", "Services", "ProxySettingsController.cs");
        var constructor = Slice(source, "public ProxySettingsController(", "public event EventHandler? StateChanged");
        var initialize = Slice(source, "public async Task<ProxySettingsInitializationResult> InitializeAsync", "public async Task<ProxySettingsSaveResult> SaveAsync");
        var save = Slice(source, "public async Task<ProxySettingsSaveResult> SaveAsync", "public void Dispose()");
        var apply = Slice(source, "private ProxySettingsInitializationResult TryApplyReadSettings", "private async Task<(PersistenceStatus Status, ProxySettings? Settings)> ReadSettingsAsync");
        var diagnostic = Slice(source, "private async Task WritePersistenceDiagnosticAsync", "private static bool TryValidate");
        var eventIds = Regex.Matches(source, "DiagnosticEventId\\.(?<id>[A-Za-z]+)")
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var write = save.IndexOf("var writeStatus = await WriteSettingsAsync", StringComparison.Ordinal);
        var failure = save.IndexOf("if (writeStatus != PersistenceStatus.Succeeded)", StringComparison.Ordinal);
        var failureReturn = save.IndexOf("return writeFailure", failure, StringComparison.Ordinal);
        var update = save.IndexOf("_clientPool.Update(settings)", StringComparison.Ordinal);

        Assert.Contains("_currentSettings = _clientPool.CurrentSettings", constructor, StringComparison.Ordinal);
        Assert.Contains("TryApplyReadSettings(ProxySettings.Default)", initialize, StringComparison.Ordinal);
        Assert.True(write >= 0 && write < failure && failure < failureReturn && failureReturn < update);
        Assert.True(update < save.IndexOf("_currentSettings = _clientPool.CurrentSettings", update, StringComparison.Ordinal));
        Assert.True(apply.IndexOf("_clientPool.Update(settings)", StringComparison.Ordinal) <
            apply.IndexOf("_currentSettings = _clientPool.CurrentSettings", StringComparison.Ordinal));
        Assert.Equal(new[] { "ProxySettingsRead", "ProxySettingsWrite" }, eventIds);
        Assert.DoesNotContain("CustomHttpProxyUri", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("OriginalString", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception.Message", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsWindow_StrictlyValidatesCustomProxyAndDisablesItsInputOutsideCustomMode()
    {
        var source = ReadSource("TransDuck.App", "Windows", "SettingsWindow.cs");
        var create = Slice(source, "private bool TryCreateProxySettings", "private bool TryCreateProvider");
        var controls = Slice(source, "private void SetProxyControlsEnabled", "private void ApplyCredentialControlsEnabledState");

        var candidate = create.IndexOf("var candidate = new ProxySettings", StringComparison.Ordinal);
        var validate = create.IndexOf("candidate.Validate()", candidate, StringComparison.Ordinal);
        var assignment = create.IndexOf("settings = candidate", validate, StringComparison.Ordinal);
        Assert.Contains("Uri.TryCreate(CustomHttpProxyUriTextBox.Text, UriKind.Absolute", create,
            StringComparison.Ordinal);
        Assert.True(candidate >= 0 && validate > candidate && assignment > validate);
        Assert.Contains("nameof(ProxyMode.CustomHttp)", controls, StringComparison.Ordinal);
        Assert.Contains("CustomHttpProxyUriTextBox.IsEnabled = isEnabled && customMode", controls,
            StringComparison.Ordinal);
        Assert.True(controls.IndexOf("if (!customMode)", StringComparison.Ordinal) <
            controls.IndexOf("CustomHttpProxyUriTextBox.Clear()", StringComparison.Ordinal));
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The required source method must be present.");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
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
