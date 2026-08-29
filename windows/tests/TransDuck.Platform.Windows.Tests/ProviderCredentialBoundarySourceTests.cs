// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class ProviderCredentialBoundarySourceTests
{
    [Fact]
    public void ProviderSettingsController_GatesGoogleBeforeEveryCredentialStorePath()
    {
        var source = ReadSource("TransDuck.App", "Services", "ProviderSettingsController.cs");
        var usesCredential = Slice(source, "private static bool UsesCredential", "private static bool TryValidate");
        var load = Slice(source, "public async Task<ProviderSettingsLoadResult> LoadAsync", "public async Task<ProviderTranslationSettingsResult> LoadForTranslationAsync");
        var translation = Slice(source, "public async Task<ProviderTranslationSettingsResult> LoadForTranslationAsync", "public async Task<PersistenceStatus> GetCredentialStatusAsync");
        var credentialStatus = Slice(source, "public async Task<PersistenceStatus> GetCredentialStatusAsync", "public async Task<ProviderSettingsSaveResult> SaveAsync");
        var save = Slice(source, "public async Task<ProviderSettingsSaveResult> SaveAsync", "public async Task<ProviderSettingsSaveResult> ClearCredentialAsync");
        var clear = Slice(source, "public async Task<ProviderSettingsSaveResult> ClearCredentialAsync", "private async Task<PersistenceStatus> ReadCredentialStatusAsync");

        Assert.Contains(
            "!string.Equals(provider.ProviderId, TranslationProviderIds.Google, StringComparison.Ordinal)",
            usesCredential,
            StringComparison.Ordinal);
        Assert.True(
            load.IndexOf("var credentialRequired = UsesCredential(configuration.DefaultProvider)", StringComparison.Ordinal) >= 0);
        Assert.True(load.IndexOf("credentialRequired", StringComparison.Ordinal) <
            load.IndexOf("ReadCredentialStatusAsync", StringComparison.Ordinal));
        Assert.True(translation.IndexOf("if (!UsesCredential(profile.Provider))", StringComparison.Ordinal) <
            translation.IndexOf("_credentialStore.GetAsync", StringComparison.Ordinal));
        Assert.True(credentialStatus.IndexOf("UsesCredential(provider)", StringComparison.Ordinal) <
            credentialStatus.IndexOf("ReadCredentialStatusAsync", StringComparison.Ordinal));
        Assert.True(save.IndexOf("!UsesCredential(profile.Provider) && !string.IsNullOrEmpty(password)", StringComparison.Ordinal) <
            save.IndexOf("_providerSettingsStore.ReadAsync", StringComparison.Ordinal));
        Assert.True(save.IndexOf("!UsesCredential(profile.Provider) && !string.IsNullOrEmpty(password)", StringComparison.Ordinal) <
            save.IndexOf("_configurationStore.WriteAsync", StringComparison.Ordinal));
        Assert.True(save.IndexOf("UsesCredential(profile.Provider) && !string.IsNullOrEmpty(password)", StringComparison.Ordinal) <
            save.IndexOf("_credentialStore.SetAsync", StringComparison.Ordinal));
        Assert.True(clear.IndexOf("if (!UsesCredential(provider))", StringComparison.Ordinal) <
            clear.IndexOf("_credentialStore.RemoveAsync", StringComparison.Ordinal));
    }

    [Fact]
    public void SettingsWindow_UsesResourceBackedGoogleNoCredentialStateAndWebProviderDefaults()
    {
        var source = ReadSource("TransDuck.App", "Windows", "SettingsWindow.xaml.cs");
        var defaults = Slice(source, "private static ProviderProfileSettings? CreateDefaultProfile", "private void SelectProvider");
        var refresh = Slice(source, "private async Task RefreshCredentialStatusAsync", "private bool TryCreateSettings");
        var controls = Slice(source, "private void SetCredentialControlsEnabled", "private bool SelectedProviderUsesCredential");
        var selection = Slice(source, "private bool SelectedProviderUsesCredential", "private static bool UsesCredential");

        Assert.Contains("TranslationProviderIds.Bing => BingWebProvider.DefaultEndpoint", defaults, StringComparison.Ordinal);
        Assert.Contains("TranslationProviderIds.Google => GoogleWebProvider.DefaultEndpoint", defaults, StringComparison.Ordinal);
        Assert.True(refresh.IndexOf("if (!UsesCredential(provider))", StringComparison.Ordinal) <
            refresh.IndexOf("_controller.GetCredentialStatusAsync", StringComparison.Ordinal));
        Assert.Contains("provider.status.credential_not_required", refresh, StringComparison.Ordinal);
        Assert.Contains("CredentialPasswordBox.IsEnabled = isEnabled", controls, StringComparison.Ordinal);
        Assert.Contains("ClearCredentialButton.IsEnabled = isEnabled", controls, StringComparison.Ordinal);
        Assert.True(controls.IndexOf("if (!isEnabled)", StringComparison.Ordinal) <
            controls.IndexOf("CredentialPasswordBox.Clear()", StringComparison.Ordinal));
        Assert.Contains("!string.Equals(providerId, TranslationProviderIds.Google, StringComparison.Ordinal)", selection,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AppRuntime_RegistersBothWebProvidersOnTheSharedTransport()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");

        Assert.Contains("new ProxyTranslationHttpClientLeaseSource(_proxyHttpClientPool)", source,
            StringComparison.Ordinal);
        Assert.Contains("Register(new BingWebProvider(_translationClientLeaseSource))", source,
            StringComparison.Ordinal);
        Assert.Contains("Register(new GoogleWebProvider(_translationClientLeaseSource))", source,
            StringComparison.Ordinal);
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
