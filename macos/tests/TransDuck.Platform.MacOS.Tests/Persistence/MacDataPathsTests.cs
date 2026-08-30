using TransDuck.Platform.MacOS.Persistence;

namespace TransDuck.Platform.MacOS.Tests.Persistence;

public sealed class MacDataPathsTests
{
    [Fact]
    public void DefaultRoot_UsesLibraryApplicationSupportBelowInjectedHomeWithoutCreatingIt()
    {
        var home = Path.Combine(Path.GetTempPath(), "TransDuck.MacDataPaths.Tests", Guid.NewGuid().ToString("N"));

        var paths = new MacDataPaths(homeDirectory: home);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(home, "Library", "Application Support", "TransDuck")),
            paths.RootDirectory);
        Assert.False(Directory.Exists(home));
    }

    [Fact]
    public void InjectedRoot_ResolvesEveryStableFileName()
    {
        var root = Path.Combine(Path.GetTempPath(), "TransDuck.MacDataPaths.Tests", Guid.NewGuid().ToString("N"));
        var paths = new MacDataPaths(root);

        Assert.Equal(Path.Combine(Path.GetFullPath(root), "configuration.v1.json"), paths.ConfigurationFilePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "provider-settings.v1.json"), paths.ProviderSettingsFilePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "query-sources.v1.json"), paths.QuerySourceSettingsFilePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "proxy-settings.v1.json"), paths.ProxySettingsFilePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "hotkey-settings.v1.json"), paths.HotkeySettingsFilePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "history.v1.jsonl"), paths.HistoryFilePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "diagnostics.v1.jsonl"), paths.DiagnosticFilePath);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void EnsureRootDirectory_CreatesAPrivateUserDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "TransDuck.MacDataPaths.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new MacDataPaths(root);

            paths.EnsureRootDirectory();

            Assert.True(Directory.Exists(root));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(root));
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root);
            }
        }
    }
}
