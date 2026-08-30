using System.Xml.Linq;
using TransDuck.Platform.MacOS.Startup;

namespace TransDuck.Platform.MacOS.Tests.Startup;

public sealed class LaunchAgentStartupServiceTests
{
    [Fact]
    public async Task EnableAndDisable_RoundTripOwnedLaunchAgent()
    {
        using var temporary = new StartupTemporaryDirectory();
        var service = temporary.CreateService("Current");

        var before = service.GetStatus();
        var enabled = await service.EnableAsync(CancellationToken.None);
        var loaded = service.GetStatus();
        var disabled = await service.DisableAsync(CancellationToken.None);

        Assert.Equal(MacStartupStatus.Disabled, before.Status);
        Assert.Equal(MacStartupStatus.Enabled, enabled.Status);
        Assert.Equal(MacStartupStatus.Enabled, loaded.Status);
        Assert.Equal(MacStartupStatus.Disabled, disabled.Status);
        Assert.False(File.Exists(service.LaunchAgentPath));
    }

    [Fact]
    public async Task Enable_WritesAnExactBackgroundLaunchContract()
    {
        using var temporary = new StartupTemporaryDirectory();
        var service = temporary.CreateService("Current");

        var enabled = await service.EnableAsync(CancellationToken.None);
        var document = XDocument.Load(service.LaunchAgentPath);
        var arguments = document.Descendants("array").Single().Elements("string")
            .Select(static element => element.Value)
            .ToArray();

        Assert.Equal(MacStartupStatus.Enabled, enabled.Status);
        Assert.Equal(2, arguments.Length);
        Assert.EndsWith(
            Path.Combine("TransDuck.app", "Contents", "MacOS", "TransDuck"),
            arguments[0],
            StringComparison.Ordinal);
        Assert.Equal("--background", arguments[1]);
    }

    [Fact]
    public async Task Enable_ReplacesOnlyRecognizedStaleTransDuckPath()
    {
        using var temporary = new StartupTemporaryDirectory();
        var oldService = temporary.CreateService("Old");
        await oldService.EnableAsync(CancellationToken.None);
        var currentService = temporary.CreateService("Current");

        var stale = currentService.GetStatus();
        var enabled = await currentService.EnableAsync(CancellationToken.None);

        Assert.Equal(MacStartupStatus.Stale, stale.Status);
        Assert.Equal(MacStartupStatus.Enabled, enabled.Status);
        Assert.Equal(MacStartupStatus.Enabled, currentService.GetStatus().Status);
    }

    [Fact]
    public async Task Enable_MigratesOwnedForegroundOnlyLaunchAgentToBackgroundMode()
    {
        using var temporary = new StartupTemporaryDirectory();
        var service = temporary.CreateService("Current");
        var executable = Path.Combine(
            temporary.Root,
            "Current",
            "TransDuck.app",
            "Contents",
            "MacOS",
            "TransDuck");
        Directory.CreateDirectory(Path.GetDirectoryName(service.LaunchAgentPath)!);
        await File.WriteAllTextAsync(service.LaunchAgentPath, """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>Label</key><string>com.transduck.app</string>
              <key>ProgramArguments</key><array><string>EXECUTABLE</string></array>
              <key>RunAtLoad</key><true/>
            </dict></plist>
            """.Replace("EXECUTABLE", executable, StringComparison.Ordinal));

        var before = service.GetStatus();
        var enabled = await service.EnableAsync(CancellationToken.None);
        var arguments = XDocument.Load(service.LaunchAgentPath)
            .Descendants("array").Single().Elements("string").ToArray();

        Assert.Equal(MacStartupStatus.Stale, before.Status);
        Assert.Equal(MacStartupStatus.Enabled, enabled.Status);
        Assert.Equal("--background", arguments[1].Value);
    }

    [Fact]
    public async Task Conflict_IsNeverOverwrittenOrDeleted()
    {
        using var temporary = new StartupTemporaryDirectory();
        var service = temporary.CreateService("Current");
        Directory.CreateDirectory(Path.GetDirectoryName(service.LaunchAgentPath)!);
        const string conflict = """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0"><dict>
              <key>Label</key><string>com.transduck.app</string>
              <key>ProgramArguments</key><array><string>/tmp/not-transduck</string></array>
              <key>RunAtLoad</key><true/>
            </dict></plist>
            """;
        await File.WriteAllTextAsync(service.LaunchAgentPath, conflict);

        var enable = await service.EnableAsync(CancellationToken.None);
        var disable = await service.DisableAsync(CancellationToken.None);

        Assert.Equal(MacStartupStatus.Conflict, enable.Status);
        Assert.Equal(MacStartupStatus.Conflict, disable.Status);
        Assert.Equal(conflict, await File.ReadAllTextAsync(service.LaunchAgentPath));
    }

    [Fact]
    public async Task Cancellation_DoesNotCreateLaunchAgent()
    {
        using var temporary = new StartupTemporaryDirectory();
        var service = temporary.CreateService("Current");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await service.EnableAsync(cancellation.Token);

        Assert.Equal(MacStartupStatus.Unavailable, result.Status);
        Assert.False(File.Exists(service.LaunchAgentPath));
    }

    [Fact]
    public async Task DevelopmentHostPath_IsNeverWrittenAsALaunchAgent()
    {
        using var temporary = new StartupTemporaryDirectory();
        var service = new LaunchAgentStartupService(
            Path.Combine(temporary.Root, "dotnet"),
            launchAgentPath: Path.Combine(temporary.Root, "Library", "LaunchAgents", "com.transduck.app.plist"));

        var result = await service.EnableAsync(CancellationToken.None);

        Assert.Equal(MacStartupStatus.Unavailable, result.Status);
        Assert.False(File.Exists(service.LaunchAgentPath));
    }
}

internal sealed class StartupTemporaryDirectory : IDisposable
{
    public StartupTemporaryDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "TransDuck.LaunchAgent.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public LaunchAgentStartupService CreateService(string versionDirectory)
    {
        var executable = Path.Combine(
            Root,
            versionDirectory,
            "TransDuck.app",
            "Contents",
            "MacOS",
            "TransDuck");
        var launchAgent = Path.Combine(Root, "Library", "LaunchAgents", LaunchAgentStartupService.FileName);
        return new LaunchAgentStartupService(executable, launchAgentPath: launchAgent);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
