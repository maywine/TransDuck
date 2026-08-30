using System.Diagnostics;

namespace TransDuck.Platform.MacOS.Capture;

public enum MacScreenCaptureStatus
{
    Succeeded,
    Cancelled,
    PermissionRequired,
    Unsupported,
    Failed,
}

public sealed class MacScreenCaptureResult : IDisposable
{
    private readonly bool _ownsFile;
    private readonly string? _cleanupDirectory;
    private int _disposeRequested;

    internal MacScreenCaptureResult(
        MacScreenCaptureStatus status,
        string? imagePath,
        bool ownsFile,
        string? cleanupDirectory = null)
    {
        Status = status;
        ImagePath = imagePath;
        _ownsFile = ownsFile;
        _cleanupDirectory = cleanupDirectory;
    }

    public MacScreenCaptureStatus Status { get; }

    public string? ImagePath { get; }

    public bool Succeeded => Status == MacScreenCaptureStatus.Succeeded && ImagePath is not null;

    public void Dispose()
    {
        if (!_ownsFile || ImagePath is null || Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        try
        {
            File.Delete(ImagePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed best-effort cleanup must not replace the capture result.
        }

        if (_cleanupDirectory is not null)
        {
            try
            {
                Directory.Delete(_cleanupDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Only the empty task-owned directory is eligible for cleanup.
            }
        }
    }
}

public interface IMacProcessRunner
{
    Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public interface IMacScreenCapturePermissionBackend
{
    bool HasAccess();

    bool RequestAccess();
}

public sealed class MacScreenCaptureService
{
    public const string ScreenCaptureExecutable = "/usr/sbin/screencapture";

    private readonly IMacProcessRunner _processRunner;
    private readonly IMacScreenCapturePermissionBackend _permissionBackend;
    private readonly string? _temporaryRoot;

    public MacScreenCaptureService(
        IMacProcessRunner? processRunner = null,
        string? temporaryRoot = null,
        IMacScreenCapturePermissionBackend? permissionBackend = null)
    {
        _processRunner = processRunner ?? new MacProcessRunner();
        _permissionBackend = permissionBackend ?? new CoreGraphicsScreenCapturePermissionBackend();
        _temporaryRoot = temporaryRoot is null ? null : Path.GetFullPath(temporaryRoot);
    }

    public async Task<MacScreenCaptureResult> CaptureRegionAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new MacScreenCaptureResult(MacScreenCaptureStatus.Cancelled, null, ownsFile: false);
        }

        try
        {
            if (!_permissionBackend.HasAccess() && !_permissionBackend.RequestAccess())
            {
                return new MacScreenCaptureResult(
                    MacScreenCaptureStatus.PermissionRequired,
                    null,
                    ownsFile: false);
            }
        }
        catch (PlatformNotSupportedException)
        {
            return new MacScreenCaptureResult(MacScreenCaptureStatus.Unsupported, null, ownsFile: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return new MacScreenCaptureResult(MacScreenCaptureStatus.Failed, null, ownsFile: false);
        }

        string? operationDirectory = null;
        string? imagePath = null;
        try
        {
            if (_temporaryRoot is null)
            {
                operationDirectory = Directory.CreateTempSubdirectory("TransDuck.Capture.").FullName;
            }
            else
            {
                Directory.CreateDirectory(_temporaryRoot);
                operationDirectory = Path.Combine(_temporaryRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(operationDirectory);
            }

            imagePath = Path.Combine(operationDirectory, "capture.png");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    operationDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var exitCode = await _processRunner.RunAsync(
                ScreenCaptureExecutable,
                ["-i", "-x", "-t", "png", imagePath],
                cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                DeleteTemporaryPath(imagePath, operationDirectory);
                return new MacScreenCaptureResult(MacScreenCaptureStatus.Failed, null, ownsFile: false);
            }

            if (!File.Exists(imagePath))
            {
                DeleteTemporaryPath(imagePath, operationDirectory);
                return new MacScreenCaptureResult(MacScreenCaptureStatus.Cancelled, null, ownsFile: false);
            }

            if (new FileInfo(imagePath).Length == 0)
            {
                DeleteTemporaryPath(imagePath, operationDirectory);
                return new MacScreenCaptureResult(MacScreenCaptureStatus.Failed, null, ownsFile: false);
            }

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(imagePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return new MacScreenCaptureResult(
                MacScreenCaptureStatus.Succeeded,
                imagePath,
                ownsFile: true,
                operationDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteTemporaryPath(imagePath, operationDirectory);
            return new MacScreenCaptureResult(MacScreenCaptureStatus.Cancelled, null, ownsFile: false);
        }
        catch (PlatformNotSupportedException)
        {
            DeleteTemporaryPath(imagePath, operationDirectory);
            return new MacScreenCaptureResult(MacScreenCaptureStatus.Unsupported, null, ownsFile: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            DeleteTemporaryPath(imagePath, operationDirectory);
            return new MacScreenCaptureResult(MacScreenCaptureStatus.Failed, null, ownsFile: false);
        }
    }

    private static void DeleteTemporaryPath(string? path, string? directory)
    {
        if (path is not null)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Cleanup remains best-effort on failure and cancellation paths.
            }
        }

        if (directory is not null)
        {
            try
            {
                Directory.Delete(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The directory is deleted only when the task left it empty.
            }
        }
    }
}

public sealed partial class CoreGraphicsScreenCapturePermissionBackend :
    IMacScreenCapturePermissionBackend
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [System.Runtime.InteropServices.LibraryImport(CoreGraphics)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
    private static partial bool CGPreflightScreenCaptureAccess();

    [System.Runtime.InteropServices.LibraryImport(CoreGraphics)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.I1)]
    private static partial bool CGRequestScreenCaptureAccess();

    public bool HasAccess()
    {
        EnsureMacOS();
        return CGPreflightScreenCaptureAccess();
    }

    public bool RequestAccess()
    {
        EnsureMacOS();
        return CGRequestScreenCaptureAccess();
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Screen Recording permission is only available on macOS.");
        }
    }
}

internal sealed class MacProcessRunner : IMacProcessRunner
{
    public async Task<int> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS screen capture is only available on macOS.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The macOS screen capture process did not start.");
        }

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or NotSupportedException)
            {
                // Cancellation still wins if the interactive process exited concurrently.
            }

            throw;
        }
    }
}
