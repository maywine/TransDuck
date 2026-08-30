using TransDuck.Platform.MacOS.Hotkeys;

namespace TransDuck.Platform.MacOS.Tests.Hotkeys;

public sealed class MacGlobalHotkeyServiceTests
{
    [Fact]
    public async Task MatchingPhysicalChord_RaisesOnceUntilKeyRelease()
    {
        var backend = new FakeKeyboardHookBackend();
        await using var service = new MacGlobalHotkeyService(backend);
        var count = 0;
        service.Pressed += (_, _) => count++;
        var status = await service.StartAsync(MacHotkeySettings.Default, CancellationToken.None);

        backend.RaisePressed(new MacKeyboardEvent(
            MacVirtualKey.D,
            MacHotkeyModifiers.Command | MacHotkeyModifiers.Option));
        backend.RaisePressed(new MacKeyboardEvent(
            MacVirtualKey.D,
            MacHotkeyModifiers.Command | MacHotkeyModifiers.Option));
        backend.RaiseReleased(new MacKeyboardEvent(MacVirtualKey.D, MacHotkeyModifiers.None));
        backend.RaisePressed(new MacKeyboardEvent(
            MacVirtualKey.D,
            MacHotkeyModifiers.Command | MacHotkeyModifiers.Option));

        Assert.Equal(MacGlobalHotkeyStatus.Registered, status);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task NonMatchingExtraOrSimulatedInput_IsIgnored()
    {
        var backend = new FakeKeyboardHookBackend();
        await using var service = new MacGlobalHotkeyService(backend);
        var count = 0;
        service.Pressed += (_, _) => count++;
        await service.StartAsync(MacHotkeySettings.Default, CancellationToken.None);

        backend.RaisePressed(new MacKeyboardEvent(MacVirtualKey.E, MacHotkeySettings.Default.Modifiers));
        backend.RaisePressed(new MacKeyboardEvent(
            MacVirtualKey.D,
            MacHotkeySettings.Default.Modifiers | MacHotkeyModifiers.Shift));
        backend.RaisePressed(new MacKeyboardEvent(
            MacVirtualKey.D,
            MacHotkeySettings.Default.Modifiers,
            IsSimulated: true));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SettingsCanChangeWithoutRestartingTheHook()
    {
        var backend = new FakeKeyboardHookBackend();
        await using var service = new MacGlobalHotkeyService(backend);
        var replacement = new MacHotkeySettings(
            MacHotkeySettingsMigration.CurrentVersion,
            MacHotkeyModifiers.Control | MacHotkeyModifiers.Shift,
            MacVirtualKey.F8);
        var count = 0;
        service.Pressed += (_, _) => count++;
        await service.StartAsync(MacHotkeySettings.Default, CancellationToken.None);

        Assert.True(service.TrySetSettings(replacement));
        backend.RaisePressed(new MacKeyboardEvent(MacVirtualKey.F8, replacement.Modifiers));

        Assert.Equal(replacement, service.Settings);
        Assert.Equal(1, count);
        Assert.Equal(1, backend.StartCount);
    }

    [Theory]
    [InlineData(FakeStartFailure.Permission, MacGlobalHotkeyStatus.PermissionRequired)]
    [InlineData(FakeStartFailure.Unsupported, MacGlobalHotkeyStatus.Unavailable)]
    [InlineData(FakeStartFailure.Other, MacGlobalHotkeyStatus.Failed)]
    public async Task Start_MapsBackendFailuresWithoutThrowing(
        FakeStartFailure failure,
        MacGlobalHotkeyStatus expected)
    {
        var backend = new FakeKeyboardHookBackend { StartFailure = failure };
        await using var service = new MacGlobalHotkeyService(backend);

        var status = await service.StartAsync(MacHotkeySettings.Default, CancellationToken.None);

        Assert.Equal(expected, status);
    }

    [Fact]
    public async Task Start_MapsCallerCancellationWithoutRegistering()
    {
        var backend = new FakeKeyboardHookBackend();
        await using var service = new MacGlobalHotkeyService(backend);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var status = await service.StartAsync(MacHotkeySettings.Default, cancellation.Token);

        Assert.Equal(MacGlobalHotkeyStatus.Unavailable, status);
    }

    [Fact]
    public async Task ConcurrentStarts_CreateOnlyOneNativeHook()
    {
        var startCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeKeyboardHookBackend { StartCompletion = startCompletion };
        await using var service = new MacGlobalHotkeyService(backend);

        var first = service.StartAsync(MacHotkeySettings.Default, CancellationToken.None);
        Assert.Equal(1, backend.StartCount);
        var second = service.StartAsync(MacHotkeySettings.Default, CancellationToken.None);
        Assert.Equal(1, backend.StartCount);
        startCompletion.SetResult();

        var statuses = await Task.WhenAll(first, second);
        Assert.All(statuses, status => Assert.Equal(MacGlobalHotkeyStatus.Registered, status));
        Assert.Equal(1, backend.StartCount);
    }

    [Fact]
    public async Task DisposeDuringStart_WaitsAndNeverReportsARegisteredHook()
    {
        var startCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeKeyboardHookBackend { StartCompletion = startCompletion };
        var service = new MacGlobalHotkeyService(backend);

        var start = service.StartAsync(MacHotkeySettings.Default, CancellationToken.None);
        var dispose = service.DisposeAsync().AsTask();
        Assert.False(dispose.IsCompleted);
        startCompletion.SetResult();

        Assert.Equal(MacGlobalHotkeyStatus.Unavailable, await start);
        await dispose;
        Assert.True(backend.StopCount >= 1);
    }
}

public enum FakeStartFailure
{
    None,
    Permission,
    Unsupported,
    Other,
}

internal sealed class FakeKeyboardHookBackend : IMacKeyboardHookBackend
{
    public event EventHandler<MacKeyboardEvent>? KeyPressed;

    public event EventHandler<MacKeyboardEvent>? KeyReleased;

    public FakeStartFailure StartFailure { get; init; }

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    public TaskCompletionSource? StartCompletion { get; init; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCount++;
        if (StartCompletion is not null)
        {
            return StartCompletion.Task.WaitAsync(cancellationToken);
        }

        return StartFailure switch
        {
            FakeStartFailure.Permission => Task.FromException(new UnauthorizedAccessException()),
            FakeStartFailure.Unsupported => Task.FromException(new PlatformNotSupportedException()),
            FakeStartFailure.Other => Task.FromException(new InvalidOperationException()),
            _ => Task.CompletedTask,
        };
    }

    public Task StopAsync()
    {
        StopCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void RaisePressed(MacKeyboardEvent keyboardEvent) => KeyPressed?.Invoke(this, keyboardEvent);

    public void RaiseReleased(MacKeyboardEvent keyboardEvent) => KeyReleased?.Invoke(this, keyboardEvent);
}
