namespace TransDuck.Platform.MacOS.Tests;

public sealed class MacAppSourceContractTests
{
    [Fact]
    public void Runtime_RegistersAllProvidersOnOneSharedProxyLeaseSource()
    {
        var source = ReadRepositoryFile("macos", "src", "TransDuck.App", "MacAppRuntime.cs");

        Assert.Contains("new ProxyTranslationHttpClientLeaseSource(_httpClientPool)", source,
            StringComparison.Ordinal);
        foreach (var provider in new[]
                 {
                     "OpenAiCompatibleProvider",
                     "DeepLProvider",
                     "OllamaProvider",
                     "BingWebProvider",
                     "GoogleWebProvider",
                     "VolcengineProvider",
                 })
        {
            Assert.Contains("_providers.Register(new " + provider + "(leaseSource))", source,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Runtime_FansOutConfiguredProvidersAndBothDictionarySources()
    {
        var source = ReadRepositoryFile("macos", "src", "TransDuck.App", "MacAppRuntime.cs");
        var settings = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "SettingsWindow.axaml");

        Assert.Contains("new LocalDictionaryProvider", source, StringComparison.Ordinal);
        Assert.Contains("new MacSystemDictionaryProvider", source, StringComparison.Ordinal);
        Assert.Contains("RunTranslationSourceAsync", source, StringComparison.Ordinal);
        Assert.Contains("RunDictionarySourceAsync", source, StringComparison.Ordinal);
        Assert.Contains("await Task.WhenAll(runs)", source, StringComparison.Ordinal);
        Assert.Contains("Revision = _state.Revision + 1", source, StringComparison.Ordinal);
        Assert.Contains("state.Revision < _lastAppliedRevision", ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "MainWindow.axaml.cs"),
            StringComparison.Ordinal);
        Assert.Contains("TranslateAsync(retry.Text, retry.QueryKind, retry.SourceKeys)", source,
            StringComparison.Ordinal);
        Assert.Contains("PrepareRetryResults(presentations)", source, StringComparison.Ordinal);
        Assert.Contains("MarkActiveSourcesCancelled()", source, StringComparison.Ordinal);
        Assert.Contains("TranslationStreamEventKind.Completed => string.Empty", source,
            StringComparison.Ordinal);
        Assert.Contains("DictionaryLookupStatus.Found => string.Empty", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("All available results completed.", source, StringComparison.Ordinal);
        Assert.Contains("MacSystemDictionaryCheckBox", settings, StringComparison.Ordinal);
        Assert.Contains("LocalDictionaryEnabledCheckBox", settings, StringComparison.Ordinal);
        Assert.Contains("HandleSaveQuerySourcesClick", settings, StringComparison.Ordinal);
        Assert.Contains("new MacSystemSpeechPlayer", source, StringComparison.Ordinal);
        Assert.Contains("result.Entry?.Term", source, StringComparison.Ordinal);
        var mainWindow = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "MainWindow.axaml");
        Assert.Contains("PronunciationTerm", mainWindow, StringComparison.Ordinal);
        Assert.Contains("HandlePronounceClick", mainWindow, StringComparison.Ordinal);
        Assert.Contains(
            "Background=\"{DynamicResource SystemControlBackgroundBaseLowBrush}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.Contains(
            "BorderBrush=\"{DynamicResource SystemControlForegroundBaseLowBrush}\"",
            mainWindow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"#F4F7FA\"", mainWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_UsesKeychainAndClosedDiagnosticsWithoutAPlaintextCredentialFallback()
    {
        var source = ReadRepositoryFile("macos", "src", "TransDuck.App", "MacAppRuntime.cs");
        var paths = ReadRepositoryFile(
            "macos", "src", "TransDuck.Platform.MacOS", "Persistence", "MacDataPaths.cs");

        Assert.Contains("MacKeychainCredentialStore", source, StringComparison.Ordinal);
        Assert.Contains("JsonLinesDiagnosticSink", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialsDirectory", paths, StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialFile", source + paths, StringComparison.Ordinal);
        Assert.DoesNotContain("DiagnosticEvent(", source.AsSpan(
            source.IndexOf("private async Task AppendHistoryAsync", StringComparison.Ordinal),
            source.IndexOf("private async Task WriteDiagnosticAsync", StringComparison.Ordinal) -
            source.IndexOf("private async Task AppendHistoryAsync", StringComparison.Ordinal)).ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void MainAndSettingsWindows_DisplayTheSharedAppAssemblyVersionWithoutHardcodingIt()
    {
        var mainMarkup = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "MainWindow.axaml");
        var mainCode = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "MainWindow.axaml.cs");
        var settingsMarkup = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "SettingsWindow.axaml");
        var settingsCode = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "SettingsWindow.axaml.cs");
        const string versionAssignment =
            "VersionTextBlock.Text = ProductVersionDisplay.FromAssembly(typeof(App).Assembly);";

        Assert.Contains("x:Name=\"VersionTextBlock\"", mainMarkup, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"VersionTextBlock\"", settingsMarkup, StringComparison.Ordinal);
        Assert.Contains(versionAssignment, mainCode, StringComparison.Ordinal);
        Assert.Contains(versionAssignment, settingsCode, StringComparison.Ordinal);
        Assert.DoesNotContain("VersionTextBlock.Text = \"v", mainCode + settingsCode,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BundleContract_PinsMenuBarIdentityPermissionsAndBothArchitectures()
    {
        var app = ReadRepositoryFile("macos", "src", "TransDuck.App", "App.axaml");
        var plist = ReadRepositoryFile("macos", "packaging", "Info.plist.in");
        var package = ReadRepositoryFile("macos", "packaging", "package-app.sh");
        var verify = ReadRepositoryFile("macos", "packaging", "TransDuck.Packaging", "Program.cs");
        var notices = ReadRepositoryFile("macos", "THIRD-PARTY-NOTICES.md");

        Assert.Contains("<TrayIcon", app, StringComparison.Ordinal);
        Assert.Contains("com.transduck.app", plist, StringComparison.Ordinal);
        Assert.Contains("<key>LSUIElement</key>", plist, StringComparison.Ordinal);
        Assert.Contains("<key>LSMinimumSystemVersion</key>", plist, StringComparison.Ordinal);
        Assert.Contains("--background", ReadRepositoryFile(
            "macos", "src", "TransDuck.Platform.MacOS", "Startup", "LaunchAgentStartupService.cs"),
            StringComparison.Ordinal);
        Assert.Contains("--smoke-test", package, StringComparison.Ordinal);
        var program = ReadRepositoryFile("macos", "src", "TransDuck.App", "Program.cs");
        Assert.Contains("RunSmokeTest()", program, StringComparison.Ordinal);
        Assert.Contains(".With(new MacOSPlatformOptions { ShowInDock = false })", program,
            StringComparison.Ordinal);
        Assert.Contains(".UseHarfBuzz()", program, StringComparison.Ordinal);
        Assert.Contains("new MacSystemDictionaryProvider()", program, StringComparison.Ordinal);
        Assert.Contains("WaitAsync(TimeSpan.FromSeconds(15))", program, StringComparison.Ordinal);
        Assert.Contains("DictionaryLookupStatus.Found or DictionaryLookupStatus.NotFound", program,
            StringComparison.Ordinal);
        Assert.Contains("osx-x64", package + verify, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", package + verify, StringComparison.Ordinal);
        Assert.Contains("libuiohook.dylib", verify, StringComparison.Ordinal);
        Assert.Contains("TransDuck.icns", package + verify, StringComparison.Ordinal);
        Assert.Contains("a41658fb2bef7503a3bcb305ab8bf849755fe906", notices,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BundleVerifier_ClosesNonSystemDylibDependenciesInsideMacOSContents()
    {
        var verify = ReadRepositoryFile("macos", "packaging", "TransDuck.Packaging", "Program.cs");

        Assert.Contains("VerifyNativeDependencyClosure(archive, runtimeIdentifier)", verify,
            StringComparison.Ordinal);
        Assert.Contains("ReadDylibDependencies", verify, StringComparison.Ordinal);
        Assert.Contains("LoadDylibCommand", verify, StringComparison.Ordinal);
        Assert.Contains("LoadWeakDylibCommand", verify, StringComparison.Ordinal);
        Assert.Contains("ReexportDylibCommand", verify, StringComparison.Ordinal);
        Assert.Contains("LazyLoadDylibCommand", verify, StringComparison.Ordinal);
        Assert.Contains("LoadUpwardDylibCommand", verify, StringComparison.Ordinal);
        Assert.Contains("/usr/lib/", verify, StringComparison.Ordinal);
        Assert.Contains("/System/Library/", verify, StringComparison.Ordinal);
        Assert.Contains("GetBundleDependencyFileName", verify, StringComparison.Ordinal);
        Assert.Contains("invalid dylib load name offset", verify, StringComparison.Ordinal);
        Assert.Contains("unterminated dylib load name", verify, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_WaitsForTrackedOperationsBeforeDisposingSharedResources()
    {
        var source = ReadRepositoryFile("macos", "src", "TransDuck.App", "MacAppRuntime.cs");
        var dispose = Slice(source, "public async ValueTask DisposeAsync()", "private async Task TranslateAsync(");

        var cancel = dispose.IndexOf("CancelCurrentOperation()", StringComparison.Ordinal);
        var lifetime = dispose.IndexOf("_lifetimeCancellation.Cancel()", StringComparison.Ordinal);
        var wait = dispose.IndexOf("await WaitForTrackedOperationsAsync()", StringComparison.Ordinal);
        var pool = dispose.IndexOf("DisposeNonFatal(_httpClientPool)", StringComparison.Ordinal);
        Assert.True(lifetime >= 0 && cancel > lifetime && wait > cancel && pool > wait);
        Assert.Contains("_trackedOperations.Add(operation)", source, StringComparison.Ordinal);
        Assert.Contains("_trackedOperations.Remove(completed)", source, StringComparison.Ordinal);
        Assert.Contains("DisposeNonFatal(_httpClientPool)", dispose, StringComparison.Ordinal);
    }

    [Fact]
    public void App_InterceptsPlatformShutdownForAsyncCleanupWithoutBlockingTheUiThread()
    {
        var source = ReadRepositoryFile("macos", "src", "TransDuck.App", "App.axaml.cs");
        var shutdown = Slice(
            source,
            "private void HandleShutdownRequested",
            "private void HandlePresentationRequested");
        var exit = Slice(source, "private void HandleDesktopExit", "}");

        Assert.Contains("desktop.ShutdownRequested += HandleShutdownRequested", source,
            StringComparison.Ordinal);
        Assert.Contains("eventArgs.Cancel = true", shutdown, StringComparison.Ordinal);
        Assert.Contains("_ = StopAsync()", shutdown, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult()", exit, StringComparison.Ordinal);

        var stop = Slice(source, "private async Task StopAsync", "private void HandleDesktopExit");
        Assert.True(
            stop.IndexOf("await runtime.DisposeAsync()", StringComparison.Ordinal) <
            stop.IndexOf("_mainWindow?.Close()", StringComparison.Ordinal));
    }

    [Fact]
    public void App_RequestsAccessibilityOnForegroundLaunchAndRefreshesAfterActivation()
    {
        var source = ReadRepositoryFile("macos", "src", "TransDuck.App", "App.axaml.cs");
        var initialize = Slice(
            source,
            "private async Task InitializeRuntimeAsync",
            "private void HandleApplicationActivated");
        var refresh = Slice(
            source,
            "private async Task RefreshAccessibilityAndHotkeyAsync",
            "private void HandleOpenRequested");

        Assert.Contains("desktop is IActivatableLifetime", source, StringComparison.Ordinal);
        Assert.Contains("activatableLifetime.Activated += HandleApplicationActivated", source,
            StringComparison.Ordinal);
        Assert.Contains("activatableLifetime.Activated -= HandleApplicationActivated", source,
            StringComparison.Ordinal);
        Assert.Contains("if (!Program.StartInBackground)", initialize, StringComparison.Ordinal);
        Assert.Contains("EnsureAccessibilityAndHotkeyAsync(prompt: true)", initialize,
            StringComparison.Ordinal);
        Assert.Contains("EnsureAccessibilityAndHotkeyAsync(prompt: false)", refresh,
            StringComparison.Ordinal);
    }

    [Fact]
    public void App_UsesOnlyTheMenuBarAndHidesWindowsWithoutStoppingTheProcess()
    {
        var program = ReadRepositoryFile("macos", "src", "TransDuck.App", "Program.cs");
        var app = ReadRepositoryFile("macos", "src", "TransDuck.App", "App.axaml.cs");
        var mainWindow = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "MainWindow.axaml.cs");
        var settingsWindow = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "SettingsWindow.axaml.cs");
        var historyWindow = ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Views", "HistoryWindow.axaml.cs");

        Assert.Contains("ShowInDock = false", program, StringComparison.Ordinal);
        Assert.Contains("ShutdownMode.OnExplicitShutdown", app, StringComparison.Ordinal);
        foreach (var window in new[] { mainWindow, settingsWindow, historyWindow })
        {
            Assert.Contains("eventArgs.Cancel = true", window, StringComparison.Ordinal);
            Assert.Contains("Hide()", window, StringComparison.Ordinal);
        }
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
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
                    return File.ReadAllText(candidate);
                }
            }
        }

        throw new FileNotFoundException("The requested repository source file was not found.");
    }
}
