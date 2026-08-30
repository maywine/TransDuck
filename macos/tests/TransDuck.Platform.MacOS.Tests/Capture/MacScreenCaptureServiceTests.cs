using TransDuck.Platform.MacOS.Capture;

namespace TransDuck.Platform.MacOS.Tests.Capture;

public sealed class MacScreenCaptureServiceTests
{
    [Fact]
    public async Task SuccessfulCapture_UsesClosedArgumentsAndDeletesOwnedImageOnDispose()
    {
        using var temporary = new CaptureTemporaryDirectory();
        var runner = new FakeCaptureProcessRunner { WriteImage = true };
        var service = new MacScreenCaptureService(runner, temporary.Root, new FakeCapturePermissionBackend());

        var result = await service.CaptureRegionAsync(CancellationToken.None);
        var path = Assert.IsType<string>(result.ImagePath);

        Assert.True(result.Succeeded);
        Assert.Equal(MacScreenCaptureService.ScreenCaptureExecutable, runner.Executable);
        Assert.Equal(new[] { "-i", "-x", "-t", "png", path }, runner.Arguments);
        Assert.True(File.Exists(path));
        Assert.StartsWith(Path.GetFullPath(temporary.Root), Path.GetFullPath(path));
        result.Dispose();
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporary.Root));
    }

    [Fact]
    public async Task UserCancellation_IsDetectedByMissingOutputFile()
    {
        using var temporary = new CaptureTemporaryDirectory();
        var runner = new FakeCaptureProcessRunner();
        var service = new MacScreenCaptureService(runner, temporary.Root, new FakeCapturePermissionBackend());

        using var result = await service.CaptureRegionAsync(CancellationToken.None);

        Assert.Equal(MacScreenCaptureStatus.Cancelled, result.Status);
        Assert.Null(result.ImagePath);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporary.Root));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public async Task ProcessFailureOrEmptyImage_LeavesNoTemporaryFile(int exitCode, bool writeEmptyImage)
    {
        using var temporary = new CaptureTemporaryDirectory();
        var runner = new FakeCaptureProcessRunner
        {
            ExitCode = exitCode,
            WriteEmptyImage = writeEmptyImage,
        };
        var service = new MacScreenCaptureService(runner, temporary.Root, new FakeCapturePermissionBackend());

        using var result = await service.CaptureRegionAsync(CancellationToken.None);

        Assert.Equal(MacScreenCaptureStatus.Failed, result.Status);
        Assert.Empty(Directory.EnumerateFileSystemEntries(temporary.Root));
    }

    [Fact]
    public async Task PreCancelledOperation_DoesNotStartProcessOrCreateDirectory()
    {
        using var temporary = new CaptureTemporaryDirectory(create: false);
        var runner = new FakeCaptureProcessRunner();
        var service = new MacScreenCaptureService(runner, temporary.Root, new FakeCapturePermissionBackend());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var result = await service.CaptureRegionAsync(cancellation.Token);

        Assert.Equal(MacScreenCaptureStatus.Cancelled, result.Status);
        Assert.Equal(0, runner.RunCount);
        Assert.False(Directory.Exists(temporary.Root));
    }

    [Fact]
    public async Task MissingPermission_RequestsAccessBeforeStartingCapture()
    {
        using var temporary = new CaptureTemporaryDirectory(create: false);
        var runner = new FakeCaptureProcessRunner();
        var permission = new FakeCapturePermissionBackend
        {
            HasPermission = false,
            RequestResult = false,
        };
        var service = new MacScreenCaptureService(runner, temporary.Root, permission);

        using var result = await service.CaptureRegionAsync(CancellationToken.None);

        Assert.Equal(MacScreenCaptureStatus.PermissionRequired, result.Status);
        Assert.Equal(1, permission.PreflightCount);
        Assert.Equal(1, permission.RequestCount);
        Assert.Equal(0, runner.RunCount);
        Assert.False(Directory.Exists(temporary.Root));
    }

    [Fact]
    public async Task ExistingPermission_DoesNotRequestAgain()
    {
        using var temporary = new CaptureTemporaryDirectory();
        var runner = new FakeCaptureProcessRunner();
        var permission = new FakeCapturePermissionBackend { HasPermission = true };
        var service = new MacScreenCaptureService(runner, temporary.Root, permission);

        using var result = await service.CaptureRegionAsync(CancellationToken.None);

        Assert.Equal(MacScreenCaptureStatus.Cancelled, result.Status);
        Assert.Equal(1, permission.PreflightCount);
        Assert.Equal(0, permission.RequestCount);
        Assert.Equal(1, runner.RunCount);
    }

    [Fact]
    public async Task DefaultTemporaryLocation_UsesAndRemovesAUniqueTaskDirectory()
    {
        var runner = new FakeCaptureProcessRunner { WriteImage = true };
        var service = new MacScreenCaptureService(
            runner,
            temporaryRoot: null,
            new FakeCapturePermissionBackend());

        var result = await service.CaptureRegionAsync(CancellationToken.None);
        var imagePath = Assert.IsType<string>(result.ImagePath);
        var operationDirectory = Path.GetDirectoryName(imagePath)!;

        Assert.StartsWith("TransDuck.Capture.", Path.GetFileName(operationDirectory),
            StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(operationDirectory));
        }

        result.Dispose();
        Assert.False(Directory.Exists(operationDirectory));
    }
}

internal sealed class FakeCapturePermissionBackend : IMacScreenCapturePermissionBackend
{
    public bool HasPermission { get; init; } = true;

    public bool RequestResult { get; init; }

    public int PreflightCount { get; private set; }

    public int RequestCount { get; private set; }

    public bool HasAccess()
    {
        PreflightCount++;
        return HasPermission;
    }

    public bool RequestAccess()
    {
        RequestCount++;
        return RequestResult;
    }
}

internal sealed class FakeCaptureProcessRunner : IMacProcessRunner
{
    public int ExitCode { get; init; }

    public bool WriteImage { get; init; }

    public bool WriteEmptyImage { get; init; }

    public int RunCount { get; private set; }

    public string? Executable { get; private set; }

    public IReadOnlyList<string> Arguments { get; private set; } = [];

    public async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RunCount++;
        Executable = executable;
        Arguments = arguments.ToArray();
        if (WriteImage)
        {
            await File.WriteAllBytesAsync(arguments[^1], [1, 2, 3], cancellationToken);
        }
        else if (WriteEmptyImage)
        {
            await File.WriteAllBytesAsync(arguments[^1], [], cancellationToken);
        }

        return ExitCode;
    }
}

internal sealed class CaptureTemporaryDirectory : IDisposable
{
    public CaptureTemporaryDirectory(bool create = true)
    {
        Root = Path.Combine(Path.GetTempPath(), "TransDuck.Capture.Tests", Guid.NewGuid().ToString("N"));
        if (create)
        {
            Directory.CreateDirectory(Root);
        }
    }

    public string Root { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
