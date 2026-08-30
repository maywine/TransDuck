// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Lookup;
using TransDuck.Platform.MacOS.Speech;

namespace TransDuck.Platform.MacOS.Tests.Speech;

public sealed class MacSystemSpeechPlayerTests
{
    [Fact]
    public async Task SpeakAsync_UsesFixedExecutableAndSendsUserTextOnlyToStandardInput()
    {
        const string text = "duck; $(not-a-command)";
        var process = new FakeSpeechProcess();
        var backend = new FakeSpeechProcessBackend(process);
        using var player = new MacSystemSpeechPlayer(backend);

        var result = await player.SpeakAsync(text, CancellationToken.None);

        Assert.Equal(SpeechPlaybackStatus.Completed, result.Status);
        Assert.Equal([MacSystemSpeechPlayer.SayExecutable], backend.Executables);
        Assert.Equal([text], process.StandardInput);
        Assert.Equal(0, process.StopCount);
    }

    [Fact]
    public async Task SpeakAsync_RejectsBlankTextAndPreCancellationWithoutStartingAProcess()
    {
        var backend = new FakeSpeechProcessBackend(new FakeSpeechProcess());
        using var player = new MacSystemSpeechPlayer(backend);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var blank = await player.SpeakAsync(" ", CancellationToken.None);
        var cancelled = await player.SpeakAsync("duck", cancellation.Token);

        Assert.Equal(SpeechPlaybackStatus.InvalidText, blank.Status);
        Assert.Equal(SpeechPlaybackStatus.Cancelled, cancelled.Status);
        Assert.Empty(backend.Executables);
    }

    [Theory]
    [InlineData(1, SpeechPlaybackStatus.Failed)]
    [InlineData(0, SpeechPlaybackStatus.Completed)]
    public async Task SpeakAsync_MapsProcessExitCode(int exitCode, SpeechPlaybackStatus expected)
    {
        var backend = new FakeSpeechProcessBackend(new FakeSpeechProcess { ExitCode = exitCode });
        using var player = new MacSystemSpeechPlayer(backend);

        var result = await player.SpeakAsync("duck", CancellationToken.None);

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData(true, SpeechPlaybackStatus.Unavailable)]
    [InlineData(false, SpeechPlaybackStatus.Failed)]
    public async Task SpeakAsync_MapsBackendStartFailures(bool unavailable, SpeechPlaybackStatus expected)
    {
        var backend = new FakeSpeechProcessBackend(new FakeSpeechProcess())
        {
            StartException = unavailable
                ? new PlatformNotSupportedException()
                : new InvalidOperationException(),
        };
        using var player = new MacSystemSpeechPlayer(backend);

        var result = await player.SpeakAsync("duck", CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Empty(backend.Executables);
    }

    [Fact]
    public async Task Cancellation_StopsTheActiveProcessAndReturnsCancelled()
    {
        var process = new FakeSpeechProcess { WaitUntilCancelled = true };
        var backend = new FakeSpeechProcessBackend(process);
        using var player = new MacSystemSpeechPlayer(backend);
        using var cancellation = new CancellationTokenSource();

        var playback = player.SpeakAsync("duck", cancellation.Token);
        await process.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var result = await playback;

        Assert.Equal(SpeechPlaybackStatus.Cancelled, result.Status);
        Assert.Equal(1, process.StopCount);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task Stop_StopsTheActiveProcessAndReturnsCancelled()
    {
        var process = new FakeSpeechProcess { WaitUntilCancelled = true };
        var backend = new FakeSpeechProcessBackend(process);
        using var player = new MacSystemSpeechPlayer(backend);

        var playback = player.SpeakAsync("duck", CancellationToken.None);
        await process.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        player.Stop();
        var result = await playback;

        Assert.Equal(SpeechPlaybackStatus.Cancelled, result.Status);
        Assert.Equal(1, process.StopCount);
    }

    [Fact]
    public async Task NewRequest_StopsTheOlderProcessBeforeCompletingTheNewOne()
    {
        var first = new FakeSpeechProcess { WaitUntilCancelled = true };
        var second = new FakeSpeechProcess();
        var backend = new FakeSpeechProcessBackend(first, second);
        using var player = new MacSystemSpeechPlayer(backend);

        var firstPlayback = player.SpeakAsync("first", CancellationToken.None);
        await first.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondResult = await player.SpeakAsync("second", CancellationToken.None);
        var firstResult = await firstPlayback;

        Assert.Equal(SpeechPlaybackStatus.Cancelled, firstResult.Status);
        Assert.Equal(SpeechPlaybackStatus.Completed, secondResult.Status);
        Assert.Equal(1, first.StopCount);
        Assert.Equal(["first"], first.StandardInput);
        Assert.Equal(["second"], second.StandardInput);
    }

    [Fact]
    public async Task Dispose_StopsActiveSpeechAndRejectsFutureRequests()
    {
        var process = new FakeSpeechProcess { WaitUntilCancelled = true };
        var backend = new FakeSpeechProcessBackend(process);
        var player = new MacSystemSpeechPlayer(backend);

        var playback = player.SpeakAsync("duck", CancellationToken.None);
        await process.WaitStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        player.Dispose();
        var cancelled = await playback;
        var unavailable = await player.SpeakAsync("after dispose", CancellationToken.None);

        Assert.Equal(SpeechPlaybackStatus.Cancelled, cancelled.Status);
        Assert.Equal(SpeechPlaybackStatus.Unavailable, unavailable.Status);
        Assert.Equal(1, process.StopCount);
        Assert.Equal(1, process.DisposeCount);
    }
}

internal sealed class FakeSpeechProcessBackend : IMacSpeechProcessBackend
{
    private readonly Queue<IMacSpeechProcess> _processes;

    public FakeSpeechProcessBackend(params IMacSpeechProcess[] processes)
    {
        _processes = new Queue<IMacSpeechProcess>(processes);
    }

    public List<string> Executables { get; } = [];

    public Exception? StartException { get; init; }

    public IMacSpeechProcess Start(string executable)
    {
        if (StartException is not null)
        {
            throw StartException;
        }

        Executables.Add(executable);
        return _processes.Dequeue();
    }
}

internal sealed class FakeSpeechProcess : IMacSpeechProcess
{
    public int ExitCode { get; init; }

    public bool WaitUntilCancelled { get; init; }

    public TaskCompletionSource<object?> WaitStarted { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public List<string> StandardInput { get; } = [];

    public int StopCount { get; private set; }

    public int DisposeCount { get; private set; }

    public Task WriteStandardInputAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StandardInput.Add(text);
        return Task.CompletedTask;
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken)
    {
        WaitStarted.TrySetResult(null);
        if (WaitUntilCancelled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        return ExitCode;
    }

    public void Stop() => StopCount++;

    public void Dispose() => DisposeCount++;
}
