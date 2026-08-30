// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Infrastructure.Persistence;
using TransDuck.Infrastructure.Tests.Persistence;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.Platform.Windows.Tests.Persistence;

public sealed class WindowsDataPathsTests
{
    [Fact]
    public void DefaultRoot_UsesTheDedicatedTransDuckAppDataDirectory()
    {
        var paths = new WindowsDataPaths();

        Assert.Equal("TransDuck", Path.GetFileName(paths.RootDirectory));
        Assert.NotEqual("Easydict", Path.GetFileName(paths.RootDirectory));
    }

    [Fact]
    public void InjectedRoot_ResolvesPathsWithoutCreatingLocalAppDataOrFiles()
    {
        using var temporary = new PersistenceTestDirectory();
        var injectedRoot = temporary.DirectoryPath("injected-root");

        var paths = new WindowsDataPaths(injectedRoot);

        Assert.Equal(Path.GetFullPath(injectedRoot), paths.RootDirectory);
        Assert.False(Directory.Exists(injectedRoot));
        Assert.Equal(Path.Combine(paths.RootDirectory, "configuration.v1.json"), paths.ConfigurationFilePath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "provider-settings.v1.json"), paths.ProviderSettingsFilePath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "query-sources.v1.json"), paths.QuerySourceSettingsFilePath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "hotkey-settings.v1.json"), paths.HotkeySettingsFilePath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "credentials"), paths.CredentialsDirectoryPath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "history.v1.jsonl"), paths.HistoryFilePath);
        Assert.Equal(Path.Combine(paths.RootDirectory, "diagnostics.v1.jsonl"), paths.DiagnosticFilePath);
    }
}
