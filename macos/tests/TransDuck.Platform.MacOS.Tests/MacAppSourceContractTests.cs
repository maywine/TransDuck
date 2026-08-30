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
        Assert.Contains("SmokeTestMode", ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Program.cs"), StringComparison.Ordinal);
        Assert.Contains("return SmokeTestMode ? 2 : 0", ReadRepositoryFile(
            "macos", "src", "TransDuck.App", "Program.cs"), StringComparison.Ordinal);
        Assert.Contains("osx-x64", package + verify, StringComparison.Ordinal);
        Assert.Contains("osx-arm64", package + verify, StringComparison.Ordinal);
        Assert.Contains("libuiohook.dylib", verify, StringComparison.Ordinal);
        Assert.Contains("TransDuck.icns", package + verify, StringComparison.Ordinal);
        Assert.Contains("a41658fb2bef7503a3bcb305ab8bf849755fe906", notices,
            StringComparison.Ordinal);
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
