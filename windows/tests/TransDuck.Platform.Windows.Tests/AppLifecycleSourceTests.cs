// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.RegularExpressions;

namespace TransDuck.Platform.Windows.Tests;

public sealed class AppLifecycleSourceTests
{
    private static readonly Regex BareAsyncInvocationPattern = new(
        "_\\s*=\\s*(?:[A-Za-z_]\\w*\\.)?[A-Za-z_]\\w*Async\\s*\\(",
        RegexOptions.CultureInvariant);
    private static readonly Regex TaskFactoryTrackingPattern = new(
        "\\b[A-Za-z_]\\w*\\s*\\(\\s*Func<Task>\\s+[A-Za-z_]\\w*\\s*\\)",
        RegexOptions.CultureInvariant);

    [Fact]
    public void AppRuntime_TracksAsyncUserOperationEntrypointsWithoutBareFireAndForget()
    {
        var source = ReadAppRuntimeCode();

        Assert.Matches("(?:HashSet|ISet)<Task>", source);
        Assert.Contains("Task.WhenAll", source, StringComparison.Ordinal);
        Assert.Matches(TaskFactoryTrackingPattern, source);
        Assert.DoesNotMatch(BareAsyncInvocationPattern, source);
    }

    [Fact]
    public void AppRuntime_AwaitsStopBeforeRequestingApplicationShutdown()
    {
        var source = ReadAppRuntimeCode();
        var stopIndex = source.IndexOf("await StopAsync()", StringComparison.Ordinal);
        var shutdownIndex = source.IndexOf("desktop.Shutdown()", StringComparison.Ordinal);

        Assert.True(stopIndex >= 0, "The exit path must await StopAsync.");
        Assert.True(shutdownIndex > stopIndex, "Application shutdown must follow StopAsync.");
    }

    private static string ReadAppRuntimeCode()
    {
        var source = File.ReadAllText(FindAppRuntimePath());
        source = Regex.Replace(source, "/\\*[\\s\\S]*?\\*/", string.Empty);
        return string.Join(
            Environment.NewLine,
            source.Split('\n').Select(line =>
            {
                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }));
    }

    private static string FindAppRuntimePath()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "windows",
                    "src",
                    "TransDuck.App",
                    "AppRuntime.cs");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException("AppRuntime.cs was not found from the test host path.");
    }
}
