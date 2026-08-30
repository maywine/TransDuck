using SharpHook;
using SharpHook.Data;

namespace TransDuck.Platform.MacOS.Hotkeys;

public sealed class SharpHookKeyboardBackend : IMacKeyboardHookBackend
{
    private readonly IGlobalHook _hook;
    private readonly object _gate = new();
    private Task? _runTask;
    private bool _started;
    private int _disposeRequested;

    public SharpHookKeyboardBackend()
        : this(new EventLoopGlobalHook())
    {
    }

    internal SharpHookKeyboardBackend(IGlobalHook hook)
    {
        _hook = hook;
        _hook.KeyPressed += HandleKeyPressed;
        _hook.KeyReleased += HandleKeyReleased;
    }

    public event EventHandler<MacKeyboardEvent>? KeyPressed;

    public event EventHandler<MacKeyboardEvent>? KeyReleased;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeRequested) != 0, this);
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("The macOS keyboard hook is only available on macOS.");
        }

        Task runTask;
        TaskCompletionSource enabled;
        EventHandler<HookEventArgs>? enabledHandler = null;
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            enabled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            enabledHandler = (_, _) =>
            {
                _hook.HookEnabled -= enabledHandler;
                enabled.TrySetResult();
            };
            _hook.HookEnabled += enabledHandler;
            runTask = _hook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true);
            _runTask = runTask;
        }

        try
        {
            var completed = await Task.WhenAny(enabled.Task, runTask).WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (ReferenceEquals(completed, runTask))
            {
                await runTask.ConfigureAwait(false);
                throw new InvalidOperationException("The macOS keyboard hook stopped before it was enabled.");
            }

            lock (_gate)
            {
                _started = true;
            }
        }
        catch
        {
            _hook.HookEnabled -= enabledHandler;
            try
            {
                _hook.Stop();
                await runTask.ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                // The original startup failure remains authoritative after best-effort native cleanup.
            }

            lock (_gate)
            {
                _runTask = null;
                _started = false;
            }

            throw;
        }
    }

    public async Task StopAsync()
    {
        Task? runTask;
        lock (_gate)
        {
            if (!_started && _runTask is null)
            {
                return;
            }

            _started = false;
            runTask = _runTask;
            _runTask = null;
        }

        _hook.Stop();
        if (runTask is not null)
        {
            await runTask.ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        finally
        {
            _hook.KeyPressed -= HandleKeyPressed;
            _hook.KeyReleased -= HandleKeyReleased;
            _hook.Dispose();
        }
    }

    private void HandleKeyPressed(object? sender, KeyboardHookEventArgs eventArgs)
    {
        if (TryMap(eventArgs, out var keyboardEvent))
        {
            KeyPressed?.Invoke(this, keyboardEvent);
        }
    }

    private void HandleKeyReleased(object? sender, KeyboardHookEventArgs eventArgs)
    {
        if (TryMap(eventArgs, out var keyboardEvent))
        {
            KeyReleased?.Invoke(this, keyboardEvent);
        }
    }

    private static bool TryMap(KeyboardHookEventArgs eventArgs, out MacKeyboardEvent keyboardEvent)
    {
        if (!TryMapKey(eventArgs.Data.KeyCode, out var key))
        {
            keyboardEvent = default!;
            return false;
        }

        keyboardEvent = new MacKeyboardEvent(
            key,
            MapModifiers(eventArgs.RawEvent.Mask),
            eventArgs.IsEventSimulated);
        return true;
    }

    private static MacHotkeyModifiers MapModifiers(EventMask mask)
    {
        var modifiers = MacHotkeyModifiers.None;
        if ((mask & (EventMask.LeftCtrl | EventMask.RightCtrl)) != 0)
        {
            modifiers |= MacHotkeyModifiers.Control;
        }

        if ((mask & (EventMask.LeftAlt | EventMask.RightAlt)) != 0)
        {
            modifiers |= MacHotkeyModifiers.Option;
        }

        if ((mask & (EventMask.LeftShift | EventMask.RightShift)) != 0)
        {
            modifiers |= MacHotkeyModifiers.Shift;
        }

        if ((mask & (EventMask.LeftMeta | EventMask.RightMeta)) != 0)
        {
            modifiers |= MacHotkeyModifiers.Command;
        }

        return modifiers;
    }

    private static bool TryMapKey(KeyCode keyCode, out MacVirtualKey key)
    {
        var name = keyCode.ToString();
        if (!name.StartsWith("Vc", StringComparison.Ordinal))
        {
            key = default;
            return false;
        }

        var suffix = name[2..];
        if (suffix.Length == 1 && suffix[0] is >= '0' and <= '9')
        {
            suffix = "Digit" + suffix;
        }

        return Enum.TryParse(suffix, ignoreCase: false, out key) && Enum.IsDefined(key);
    }
}
