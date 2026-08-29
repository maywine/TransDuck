// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class AppStartupSourceTests
{
    [Fact]
    public void App_AcquiresTheSingleInstanceGuardBeforeConstructingAppRuntime()
    {
        var source = ReadSource("TransDuck.App", "App.xaml.cs");
        var startup = source.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        var exit = source.IndexOf("protected override void OnExit", startup, StringComparison.Ordinal);
        var guard = source.IndexOf("TryAcquireSessionMutex()", startup, StringComparison.Ordinal);
        var runtime = source.IndexOf("new AppRuntime()", startup, StringComparison.Ordinal);

        Assert.True(startup >= 0, "The application startup path must be present.");
        Assert.True(exit > startup, "The startup path must be bounded before OnExit.");
        Assert.True(guard > startup && guard < runtime);
        Assert.True(runtime > guard && runtime < exit);
    }

    [Fact]
    public void App_UsesTheDedicatedTransDuckSessionMutex()
    {
        var source = ReadSource("TransDuck.App", "App.xaml.cs");

        Assert.Contains("Local\\\\TransDuck.Windows.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Local\\\\Easydict.Windows.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRuntime_ConstructsPassesAndDisposesTheStartupController()
    {
        var source = ReadSource("TransDuck.App", "AppRuntime.cs");
        var constructor = source.IndexOf("public AppRuntime()", StringComparison.Ordinal);
        var startMethod = source.IndexOf("public Task StartAsync()", constructor, StringComparison.Ordinal);
        var construction = source.IndexOf("new StartupSettingsController(", constructor, StringComparison.Ordinal);
        var platformService = source.IndexOf("new RegistryRunStartupRegistrationService()", construction, StringComparison.Ordinal);
        var disposeCore = source.IndexOf("private void DisposeCore()", startMethod, StringComparison.Ordinal);
        var disposeHelper = source.IndexOf("private static void DisposeNonFatal", disposeCore, StringComparison.Ordinal);
        var disposed = source.IndexOf("DisposeNonFatal(_startupSettingsController)", disposeCore, StringComparison.Ordinal);
        var settingsWindow = source.IndexOf("new SettingsWindow(", startMethod, StringComparison.Ordinal);
        var passedToWindow = source.IndexOf("_startupSettingsController", settingsWindow, StringComparison.Ordinal);

        Assert.True(constructor >= 0 && startMethod > constructor);
        Assert.True(construction > constructor && construction < startMethod);
        Assert.True(platformService > construction && platformService < startMethod);
        Assert.True(settingsWindow > startMethod);
        Assert.True(passedToWindow > settingsWindow);
        Assert.True(disposeCore > startMethod && disposeHelper > disposeCore);
        Assert.True(disposed > disposeCore && disposed < disposeHelper);
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
