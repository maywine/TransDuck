using TransDuck.Platform.MacOS.Persistence;

namespace TransDuck.Platform.MacOS.Tests;

public sealed class MacPlatformAssemblyBoundaryTests
{
    [Fact]
    public void PlatformAssembly_DoesNotReferenceAvaloniaWpfOrWindowsSdk()
    {
        var references = typeof(MacDataPaths).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();
        var forbidden = references.Where(name =>
            name.StartsWith("Avalonia", StringComparison.Ordinal) ||
            name.Equals("PresentationCore", StringComparison.Ordinal) ||
            name.Equals("PresentationFramework", StringComparison.Ordinal) ||
            name.Equals("Microsoft.Windows.SDK.NET", StringComparison.Ordinal));

        Assert.Empty(forbidden);
        Assert.Contains("SharpHook", references);
    }

    [Fact]
    public void KeychainStore_UsesSecurityFrameworkInsteadOfAPlaintextOrCommandLineFallback()
    {
        var source = ReadRepositoryFile(
            "macos",
            "src",
            "TransDuck.Platform.MacOS",
            "Persistence",
            "SecurityFrameworkKeychainBackend.cs");

        Assert.Contains("SecItemCopyMatching", source, StringComparison.Ordinal);
        Assert.Contains("SecItemAdd", source, StringComparison.Ordinal);
        Assert.Contains("SecItemUpdate", source, StringComparison.Ordinal);
        Assert.Contains("SecItemDelete", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/usr/bin/security", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessStartInfo", source, StringComparison.Ordinal);
    }

    [Fact]
    public void VisionBackend_ExplicitlyLoadsFrameworkBeforeResolvingObjectiveCClasses()
    {
        var source = ReadRepositoryFile(
            "macos",
            "src",
            "TransDuck.Platform.MacOS",
            "Ocr",
            "VisionOcrService.cs");
        var load = source.IndexOf("Frameworks.EnsureLoaded()", StringComparison.Ordinal);
        var resolve = source.IndexOf("GetClass(\"VNRecognizeTextRequest\")", StringComparison.Ordinal);

        Assert.True(load >= 0 && resolve > load);
        Assert.Contains("NativeLibrary.Load(Vision)", source, StringComparison.Ordinal);
        Assert.Contains("NativeLibrary.Load(Foundation)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreFoundationDictionaries_UseTypeCallbacksForRetainAndValueEquality()
    {
        var source = ReadRepositoryFile(
            "macos",
            "src",
            "TransDuck.Platform.MacOS",
            "Interop",
            "CoreFoundationNative.cs");

        Assert.Contains("kCFTypeDictionaryKeyCallBacks", source, StringComparison.Ordinal);
        Assert.Contains("kCFTypeDictionaryValueCallBacks", source, StringComparison.Ordinal);
        Assert.Contains("DictionaryCallbacks.KeyCallbacks", source, StringComparison.Ordinal);
        Assert.Contains("DictionaryCallbacks.ValueCallbacks", source, StringComparison.Ordinal);
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
