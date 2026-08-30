// Copyright (c) 2026 maywine. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using TransDuck.Core.Lookup;

namespace TransDuck.Platform.MacOS.Speech;

/// <summary>
/// Creates the process used to play text through the macOS system voice.
/// </summary>
public interface IMacSpeechProcessBackend
{
    IMacSpeechProcess Start(string executable);
}

/// <summary>
/// Represents one cancellable system speech process.
/// </summary>
public interface IMacSpeechProcess : IDisposable
{
    Task WriteStandardInputAsync(string text, CancellationToken cancellationToken);

    Task<int> WaitForExitAsync(CancellationToken cancellationToken);

    void Stop();
}

/// <summary>
/// Plays text with the macOS <c>say</c> utility without placing user text on a command line.
/// </summary>
public sealed class MacSystemSpeechPlayer : ISystemSpeechPlayer
{
    public const string SayExecutable = "/usr/bin/say";

    private readonly IMacSpeechProcessBackend _backend;
    private readonly object _stateGate = new();
    private readonly object _startGate = new();
    private ActivePlayback? _activePlayback;
    private long _requestVersion;
    private bool _disposeRequested;

    public MacSystemSpeechPlayer(IMacSpeechProcessBackend? backend = null)
    {
        _backend = backend ?? new MacSayProcessBackend();
    }

    public async Task<SpeechPlaybackResult> SpeakAsync(
        string text,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Result(SpeechPlaybackStatus.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result(SpeechPlaybackStatus.InvalidText);
        }

        ActivePlayback? previous;
        long requestVersion;
        lock (_stateGate)
        {
            if (_disposeRequested)
            {
                return Result(SpeechPlaybackStatus.Unavailable);
            }

            requestVersion = ++_requestVersion;
            previous = _activePlayback;
            _activePlayback = null;
        }

        StopPlaybackNoThrow(previous);
        var startup = StartPlayback(requestVersion, cancellationToken, out var playback);
        if (startup is { } startupResult)
        {
            return startupResult;
        }

        var activePlayback = playback!;
        var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            activePlayback.StopCancellation.Token);
        try
        {
            await activePlayback.Process.WriteStandardInputAsync(text, linkedCancellation.Token)
                .ConfigureAwait(false);
            var exitCode = await activePlayback.Process.WaitForExitAsync(linkedCancellation.Token)
                .ConfigureAwait(false);
            return linkedCancellation.IsCancellationRequested
                ? Result(SpeechPlaybackStatus.Cancelled)
                : Result(exitCode == 0 ? SpeechPlaybackStatus.Completed : SpeechPlaybackStatus.Failed);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
            if (!activePlayback.StopCancellation.IsCancellationRequested)
            {
                StopPlaybackNoThrow(activePlayback);
            }

            return Result(SpeechPlaybackStatus.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            if (linkedCancellation.IsCancellationRequested)
            {
                return Result(SpeechPlaybackStatus.Cancelled);
            }

            StopPlaybackNoThrow(activePlayback);
            return Result(SpeechPlaybackStatus.Failed);
        }
        finally
        {
            try
            {
                linkedCancellation.Dispose();
            }
            finally
            {
                CompletePlayback(activePlayback);
            }
        }
    }

    public void Stop()
    {
        ActivePlayback? playback;
        lock (_stateGate)
        {
            _requestVersion++;
            playback = _activePlayback;
            _activePlayback = null;
        }

        StopPlaybackNoThrow(playback);
    }

    public void Dispose()
    {
        ActivePlayback? playback;
        lock (_stateGate)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
            _requestVersion++;
            playback = _activePlayback;
            _activePlayback = null;
        }

        StopPlaybackNoThrow(playback);
    }

    private SpeechPlaybackResult? StartPlayback(
        long requestVersion,
        CancellationToken cancellationToken,
        out ActivePlayback? playback)
    {
        playback = null;
        lock (_startGate)
        {
            var unavailable = false;
            lock (_stateGate)
            {
                unavailable = _disposeRequested;
                if (!unavailable && requestVersion != _requestVersion)
                {
                    return Result(SpeechPlaybackStatus.Cancelled);
                }
            }

            if (unavailable)
            {
                return Result(SpeechPlaybackStatus.Unavailable);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Result(SpeechPlaybackStatus.Cancelled);
            }

            try
            {
                playback = new ActivePlayback(_backend.Start(SayExecutable));
            }
            catch (Exception exception) when (
                exception is PlatformNotSupportedException or FileNotFoundException or Win32Exception)
            {
                return Result(SpeechPlaybackStatus.Unavailable);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                return Result(SpeechPlaybackStatus.Failed);
            }

            SpeechPlaybackStatus? rejection = null;
            lock (_stateGate)
            {
                if (_disposeRequested)
                {
                    rejection = SpeechPlaybackStatus.Unavailable;
                }
                else if (requestVersion != _requestVersion || cancellationToken.IsCancellationRequested)
                {
                    rejection = SpeechPlaybackStatus.Cancelled;
                }
                else
                {
                    _activePlayback = playback;
                }
            }

            if (rejection is not { } status)
            {
                return null;
            }

            StopPlaybackNoThrow(playback);
            playback.Dispose();
            playback = null;
            return Result(status);
        }
    }

    private void CompletePlayback(ActivePlayback playback)
    {
        lock (_stateGate)
        {
            if (ReferenceEquals(_activePlayback, playback))
            {
                _activePlayback = null;
            }
        }

        playback.Dispose();
    }

    private static void StopPlaybackNoThrow(ActivePlayback? playback)
    {
        if (playback is null)
        {
            return;
        }

        try
        {
            playback.StopCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            playback.Process.Stop();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Cancellation is still terminal when process cleanup races with a natural exit.
        }
    }

    private static SpeechPlaybackResult Result(SpeechPlaybackStatus status) => new(status);

    private sealed class ActivePlayback(IMacSpeechProcess process) : IDisposable
    {
        public IMacSpeechProcess Process { get; } = process;

        public CancellationTokenSource StopCancellation { get; } = new();

        public void Dispose()
        {
            StopCancellation.Dispose();
            Process.Dispose();
        }
    }
}

internal sealed class MacSayProcessBackend : IMacSpeechProcessBackend
{
    public IMacSpeechProcess Start(string executable)
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS system speech is only available on macOS.");
        }

        if (!string.Equals(executable, MacSystemSpeechPlayer.SayExecutable, StringComparison.Ordinal))
        {
            throw new ArgumentException("The macOS speech executable is not supported.", nameof(executable));
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = MacSystemSpeechPlayer.SayExecutable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
            },
        };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The macOS system speech process did not start.");
            }

            return new MacSayProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}

internal sealed class MacSayProcess(Process process) : IMacSpeechProcess
{
    private readonly Process _process = process;

    public async Task WriteStandardInputAsync(string text, CancellationToken cancellationToken)
    {
        await _process.StandardInput.WriteAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        _process.StandardInput.Close();
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public void Stop()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public void Dispose() => _process.Dispose();
}
