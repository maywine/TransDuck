using TransDuck.Core.Persistence;
using System.Diagnostics;
using TransDuck.Platform.MacOS.Capture;
using TransDuck.Core.Lookup;
using TransDuck.Platform.MacOS.Dictionary;
using TransDuck.Platform.MacOS.Ocr;
using TransDuck.Platform.MacOS.Persistence;
using TransDuck.Platform.MacOS.Selection;
using TransDuck.Platform.MacOS.Startup;
using SharpHook.Providers;

namespace TransDuck.Platform.MacOS.Tests;

public sealed class NativeMacSmokeTests
{
    [MacOSFact]
    public async Task VisionFramework_RecognizesEnglishFixtureOnMacOS()
    {
        var result = await new VisionOcrService().RecognizeAsync(
            FindRepositoryFile("windows", "tests", "TransDuck.Platform.Windows.Tests", "Fixtures", "clean-en.png"),
            "en-US",
            CancellationToken.None);

        Assert.Equal(MacOcrStatus.Succeeded, result.Status);
        Assert.Contains("OCR", result.Text!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("314", result.Text!, StringComparison.Ordinal);
    }

    [MacOSFact]
    public async Task VisionFramework_RecognizesSimplifiedChineseFixtureOnMacOS()
    {
        var result = await new VisionOcrService().RecognizeAsync(
            FindRepositoryFile("windows", "tests", "TransDuck.Platform.Windows.Tests", "Fixtures", "clean-zh.png"),
            "zh-Hans",
            CancellationToken.None);

        Assert.Equal(MacOcrStatus.Succeeded, result.Status);
        Assert.Contains("314", result.Text!, StringComparison.Ordinal);
        Assert.Contains(result.Text!, static character => character is >= '\u4e00' and <= '\u9fff');
    }

    [MacOSFact]
    public async Task SecurityFramework_KeychainRoundTripsAndRemovesUniqueSmokeItemOnMacOS()
    {
        using var store = new MacKeychainCredentialStore();
        var key = new CredentialKey(
            "openai-compatible",
            "native-smoke-" + Guid.NewGuid().ToString("N"));
        using var secret = new CredentialSecret("non-secret-native-smoke-value");

        try
        {
            var write = await store.SetAsync(key, secret, CancellationToken.None);
            var read = await store.GetAsync(key, CancellationToken.None);
            Assert.Equal(PersistenceStatus.Succeeded, write.Status);
            Assert.True(read.Succeeded);
            using (read.Value!)
            {
                Assert.Equal(secret.Reveal().Length, read.Value!.Reveal().Length);
            }
        }
        finally
        {
            var remove = await store.RemoveAsync(key, CancellationToken.None);
            Assert.True(remove.Status is PersistenceStatus.Succeeded or PersistenceStatus.NotFound);
        }
    }

    [MacOSFact]
    public void AccessibilityFramework_ReturnsAClosedPermissionStateOnMacOS()
    {
        var service = new MacAccessibilitySelectionService();
        var trusted = service.EnsurePermission(prompt: false);
        var result = service.ReadSelectedText(promptForPermission: false);

        if (!trusted)
        {
            Assert.Equal(MacSelectionStatus.PermissionRequired, result.Status);
        }
        else
        {
            Assert.NotEqual(MacSelectionStatus.PermissionRequired, result.Status);
        }
    }

    [MacOSFact]
    public void CoreGraphics_ScreenCapturePreflightLoadsWithoutPromptOnMacOS()
    {
        _ = new CoreGraphicsScreenCapturePermissionBackend().HasAccess();
    }

    [MacOSFact]
    public void SharpHookNativeLibrary_LoadsAndChecksAccessibilityWithoutPromptOnMacOS()
    {
        _ = UioHookProvider.Instance.IsAxApiEnabled(promptUserIfDisabled: false);
    }

    [MacOSFact]
    public async Task DictionaryServices_LoadsAndQueriesActiveSystemDictionariesOnMacOS()
    {
        var result = await new MacSystemDictionaryProvider().LookupAsync(
            "dictionary",
            dataFilePath: null,
            CancellationToken.None);

        Assert.True(result.Status is DictionaryLookupStatus.Found or DictionaryLookupStatus.NotFound);
        if (result.Status == DictionaryLookupStatus.Found)
        {
            Assert.NotNull(result.Entry);
            Assert.False(string.IsNullOrWhiteSpace(result.Entry!.ToDisplayText()));
        }
    }

    [MacOSFact]
    public async Task LaunchAgentPlist_IsAcceptedByMacOSPlutilWithoutTouchingUserLaunchAgents()
    {
        var root = Path.Combine(Path.GetTempPath(), "TransDuck.NativeLaunchAgent.", Guid.NewGuid().ToString("N"));
        var launchAgentPath = Path.Combine(root, "com.transduck.app.plist");
        var executablePath = Path.Combine(root, "TransDuck.app", "Contents", "MacOS", "TransDuck");
        try
        {
            var service = new LaunchAgentStartupService(executablePath, launchAgentPath: launchAgentPath);
            var enabled = await service.EnableAsync(CancellationToken.None);
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "/usr/bin/plutil",
                UseShellExecute = false,
                ArgumentList = { "-lint", launchAgentPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException("plutil did not start.");
            await process.WaitForExitAsync();

            Assert.Equal(MacStartupStatus.Enabled, enabled.Status);
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string FindRepositoryFile(params string[] relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("The requested native smoke-test fixture was not found.");
    }
}

internal sealed class MacOSFactAttribute : FactAttribute
{
    public MacOSFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Native macOS framework smoke test.";
        }
    }
}
