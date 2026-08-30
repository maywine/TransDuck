namespace TransDuck.Platform.MacOS.Hotkeys;

public sealed record MacKeyboardEvent(
    MacVirtualKey Key,
    MacHotkeyModifiers Modifiers,
    bool IsSimulated = false);

public interface IMacKeyboardHookBackend : IAsyncDisposable
{
    event EventHandler<MacKeyboardEvent>? KeyPressed;

    event EventHandler<MacKeyboardEvent>? KeyReleased;

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

public enum MacGlobalHotkeyStatus
{
    Registered,
    Invalid,
    PermissionRequired,
    Unavailable,
    Failed,
}

public sealed class MacGlobalHotkeyService : IAsyncDisposable
{
    private readonly IMacKeyboardHookBackend _backend;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private MacHotkeySettings _settings = MacHotkeySettings.Default;
    private bool _latched;
    private bool _started;
    private int _disposeRequested;

    public MacGlobalHotkeyService(IMacKeyboardHookBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _backend.KeyPressed += HandleKeyPressed;
        _backend.KeyReleased += HandleKeyReleased;
    }

    public event EventHandler? Pressed;

    public MacHotkeySettings Settings
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public async Task<MacGlobalHotkeyStatus> StartAsync(
        MacHotkeySettings settings,
        CancellationToken cancellationToken)
    {
        if (!TrySetSettings(settings))
        {
            return MacGlobalHotkeyStatus.Invalid;
        }

        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return MacGlobalHotkeyStatus.Unavailable;
        }

        var enteredStartGate = false;
        try
        {
            await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            enteredStartGate = true;
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                return MacGlobalHotkeyStatus.Unavailable;
            }

            lock (_gate)
            {
                if (_started)
                {
                    return MacGlobalHotkeyStatus.Registered;
                }
            }

            await _backend.StartAsync(cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                await _backend.StopAsync().ConfigureAwait(false);
                return MacGlobalHotkeyStatus.Unavailable;
            }

            lock (_gate)
            {
                _started = true;
            }

            return MacGlobalHotkeyStatus.Registered;
        }
        catch (UnauthorizedAccessException)
        {
            return MacGlobalHotkeyStatus.PermissionRequired;
        }
        catch (PlatformNotSupportedException)
        {
            return MacGlobalHotkeyStatus.Unavailable;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MacGlobalHotkeyStatus.Unavailable;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return MacGlobalHotkeyStatus.Failed;
        }
        finally
        {
            if (enteredStartGate)
            {
                _startGate.Release();
            }
        }
    }

    public bool TrySetSettings(MacHotkeySettings settings)
    {
        if (settings is null)
        {
            return false;
        }

        try
        {
            settings.Validate();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }

        lock (_gate)
        {
            _settings = settings;
            _latched = false;
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        _backend.KeyPressed -= HandleKeyPressed;
        _backend.KeyReleased -= HandleKeyReleased;
        await _startGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _backend.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Releasing the native hook must not prevent the remaining app shutdown.
        }

        try
        {
            await _backend.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _startGate.Release();
        }
    }

    private void HandleKeyPressed(object? sender, MacKeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.IsSimulated || Volatile.Read(ref _disposeRequested) != 0)
        {
            return;
        }

        var shouldRaise = false;
        lock (_gate)
        {
            if (_started && !_latched && keyboardEvent.Key == _settings.Key &&
                keyboardEvent.Modifiers == _settings.Modifiers)
            {
                _latched = true;
                shouldRaise = true;
            }
        }

        if (shouldRaise)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleKeyReleased(object? sender, MacKeyboardEvent keyboardEvent)
    {
        lock (_gate)
        {
            if (keyboardEvent.Key == _settings.Key)
            {
                _latched = false;
            }
        }
    }
}
