using System.Diagnostics;
using System.IO.Compression;
using TransDuck.Packaging;

namespace TransDuck.Platform.MacOS.Tests;

public sealed class MacPackageArchiveTests
{
    [MacOSFact]
    public async Task SignedBundle_ZipRoundTripPreservesPermissionsLinksAndResourceSeal()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), "TransDuck.PackageTest." + Guid.NewGuid().ToString("N"));
        try
        {
            var app = Path.Combine(root, "TransDuck.app");
            var macos = Path.Combine(app, "Contents", "MacOS");
            var runtime = Path.Combine(app, "Contents", "Resources", "Runtime");
            Directory.CreateDirectory(macos);
            Directory.CreateDirectory(Path.Combine(runtime, "en"));
            File.Copy("/bin/echo", Path.Combine(macos, "TransDuck"));
            File.SetUnixFileMode(Path.Combine(macos, "TransDuck"), (UnixFileMode)0x1ed);
            File.WriteAllText(Path.Combine(runtime, "fixture.dll"), "managed payload fixture");
            File.WriteAllText(Path.Combine(runtime, "en", "fixture.resources.dll"), "satellite fixture");
            File.CreateSymbolicLink(Path.Combine(macos, "fixture.dll"), "../Resources/Runtime/fixture.dll");
            Directory.CreateSymbolicLink(Path.Combine(macos, "en"), "../Resources/Runtime/en");
            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), """
                <?xml version="1.0" encoding="UTF-8"?>
                <plist version="1.0"><dict>
                  <key>CFBundleIdentifier</key><string>com.transduck.packaging-test</string>
                  <key>CFBundleExecutable</key><string>TransDuck</string>
                  <key>CFBundlePackageType</key><string>APPL</string>
                </dict></plist>
                """);

            await RunSuccessfullyAsync("/usr/bin/codesign", "--force", "--sign", "-", "--timestamp=none", app);
            var zip = Path.Combine(root, "package.zip");
            Assert.Equal(0, Program.Main(["pack", app, zip]));
            using (var archive = ZipFile.OpenRead(zip))
            {
                Assert.Equal(0x81ed, UnixMode(archive, "TransDuck.app/Contents/MacOS/TransDuck"));
                Assert.Equal(0xa1ff, UnixMode(archive, "TransDuck.app/Contents/MacOS/fixture.dll"));
                Assert.Equal(0xa1ff, UnixMode(archive, "TransDuck.app/Contents/MacOS/en"));
                Assert.Equal(0x8000, UnixMode(archive, "TransDuck.app/Contents/Resources/Runtime/fixture.dll") & 0xf000);
                Assert.Null(archive.GetEntry("TransDuck.app/Contents/MacOS/en/fixture.resources.dll"));
            }

            var extracted = Path.Combine(root, "extracted");
            await RunSuccessfullyAsync("/usr/bin/ditto", "-x", "-k", zip, extracted);
            var extractedApp = Path.Combine(extracted, "TransDuck.app");
            var extractedMacos = Path.Combine(extractedApp, "Contents", "MacOS");
            Assert.True(File.GetUnixFileMode(Path.Combine(extractedMacos, "TransDuck"))
                .HasFlag(UnixFileMode.UserExecute));
            Assert.Equal("../Resources/Runtime/fixture.dll", new FileInfo(Path.Combine(extractedMacos, "fixture.dll")).LinkTarget);
            Assert.Equal("../Resources/Runtime/en", new DirectoryInfo(Path.Combine(extractedMacos, "en")).LinkTarget);
            Assert.Equal("managed payload fixture", File.ReadAllText(Path.Combine(extractedMacos, "fixture.dll")));
            await RunSuccessfullyAsync("/usr/bin/codesign", "--verify", "--deep", "--strict", "--all-architectures", extractedApp);

            File.WriteAllText(Path.Combine(extractedMacos, "fixture.dll"), "modified fixture");
            var tampered = await RunAsync("/usr/bin/codesign", "--verify", "--deep", "--strict", extractedApp);
            Assert.NotEqual(0, tampered.ExitCode);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static int UnixMode(ZipArchive archive, string name) =>
        (Assert.IsType<ZipArchiveEntry>(archive.GetEntry(name)).ExternalAttributes >> 16) & 0xffff;

    private static async Task RunSuccessfullyAsync(string command, params string[] arguments)
    {
        var result = await RunAsync(command, arguments);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string command, params string[] arguments)
    {
        var start = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Process did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        return (process.ExitCode, await output + await error);
    }
}
