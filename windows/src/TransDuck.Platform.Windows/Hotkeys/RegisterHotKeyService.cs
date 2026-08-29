using System.ComponentModel;
using System.Runtime.InteropServices;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Hotkeys;

/// <summary>
/// Registers one global hotkey and restores it after a resumed desktop session.
/// </summary>
public sealed class RegisterHotKeyService : IDisposable
{
    // Application-defined IDs must stay below 0xC000; 0xC000-0xFFFF are atom IDs.
    private const int HotkeyIdentifier = 0x4544;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private readonly NativeMessageWindow _messageWindow;
    private GlobalHotkey? _requestedHotkey;
    private bool _isRegistered;
    private bool _disposed;

    public RegisterHotKeyService(NativeMessageWindow messageWindow)
    {
        _messageWindow = messageWindow;
        _messageWindow.MessageReceived += HandleMessage;
    }

    public event EventHandler? Pressed;

    public GlobalHotkey? RequestedHotkey => _requestedHotkey;

    public bool IsRegistered => _isRegistered;

    public HotkeyRegistrationResult Register(GlobalHotkey hotkey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(hotkey);

        if (_isRegistered && _requestedHotkey == hotkey)
        {
            return HotkeyRegistrationResult.AlreadyRegistered(hotkey);
        }

        var previousRequestedHotkey = _requestedHotkey;
        var wasRegistered = _isRegistered;
        var unregistration = Unregister();
        if (!unregistration.Succeeded)
        {
            return HotkeyRegistrationResult.Failed(hotkey, unregistration.ErrorMessage!);
        }

        var registration = TryRegisterHotkey(hotkey);
        if (registration.Status == HotkeyRegistrationStatus.Registered)
        {
            _requestedHotkey = hotkey;
            _isRegistered = true;
            return registration;
        }

        if (!wasRegistered)
        {
            // Preserve first-registration resume behavior, while retaining a prior request on replacement.
            _requestedHotkey ??= hotkey;
            return registration;
        }

        return RestorePreviousHotkeyAfterFailedReplacement(
            hotkey,
            previousRequestedHotkey,
            registration);
    }

    public HotkeyUnregistrationResult Unregister()
    {
        if (!_isRegistered)
        {
            return HotkeyUnregistrationResult.NotRegistered();
        }

        if (!Win32HotkeyNative.UnregisterHotKey(_messageWindow.Handle, HotkeyIdentifier))
        {
            var error = Marshal.GetLastWin32Error();
            return HotkeyUnregistrationResult.Failed(new Win32Exception(error).Message);
        }

        _isRegistered = false;
        return HotkeyUnregistrationResult.Unregistered();
    }

    public HotkeyRegistrationResult RestoreAfterPowerResume()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_requestedHotkey is not { } hotkey)
        {
            return HotkeyRegistrationResult.NoRequestedHotkey();
        }

        var unregistration = Unregister();
        if (!unregistration.Succeeded)
        {
            return HotkeyRegistrationResult.Failed(hotkey, unregistration.ErrorMessage!);
        }

        return RegisterRequestedHotkey(hotkey);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Unregister();
        _messageWindow.MessageReceived -= HandleMessage;
        _disposed = true;
    }

    private HotkeyRegistrationResult RegisterRequestedHotkey()
    {
        if (_requestedHotkey is not { } hotkey)
        {
            return HotkeyRegistrationResult.NoRequestedHotkey();
        }

        return RegisterRequestedHotkey(hotkey);
    }

    private HotkeyRegistrationResult RegisterRequestedHotkey(GlobalHotkey hotkey)
    {
        var registration = TryRegisterHotkey(hotkey);
        if (registration.Status == HotkeyRegistrationStatus.Registered)
        {
            _isRegistered = true;
        }

        return registration;
    }

    private HotkeyRegistrationResult TryRegisterHotkey(GlobalHotkey hotkey)
    {
        if (Win32HotkeyNative.RegisterHotKey(
                _messageWindow.Handle,
                HotkeyIdentifier,
                hotkey.Modifiers | HotkeyModifiers.NoRepeat,
                hotkey.VirtualKey))
        {
            return HotkeyRegistrationResult.Registered(hotkey);
        }

        var error = Marshal.GetLastWin32Error();
        return error == ErrorHotkeyAlreadyRegistered
            ? HotkeyRegistrationResult.Conflict(hotkey)
            : HotkeyRegistrationResult.Failed(hotkey, new Win32Exception(error).Message);
    }

    private HotkeyRegistrationResult RestorePreviousHotkeyAfterFailedReplacement(
        GlobalHotkey replacementHotkey,
        GlobalHotkey? previousRequestedHotkey,
        HotkeyRegistrationResult replacementRegistration)
    {
        if (previousRequestedHotkey is not { } previousHotkey)
        {
            _isRegistered = false;
            return HotkeyRegistrationResult.Failed(
                replacementHotkey,
                "无法恢复此前的全局快捷键，因为注册状态不一致。");
        }

        var restoration = TryRegisterHotkey(previousHotkey);
        if (restoration.Status == HotkeyRegistrationStatus.Registered)
        {
            _requestedHotkey = previousHotkey;
            _isRegistered = true;
            return replacementRegistration;
        }

        _requestedHotkey = previousHotkey;
        _isRegistered = false;
        return HotkeyRegistrationResult.Failed(
            replacementHotkey,
            "无法注册请求的全局快捷键，也无法恢复此前的全局快捷键。");
    }

    private void HandleMessage(object? sender, NativeWindowMessageEventArgs args)
    {
        if (args.Message == Win32HotkeyNative.WmHotkey && args.WParam.ToInt32() == HotkeyIdentifier)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
        }
    }
}

public sealed record GlobalHotkey(HotkeyModifiers Modifiers, uint VirtualKey);

public sealed record HotkeyRegistrationResult(
    HotkeyRegistrationStatus Status,
    GlobalHotkey? Hotkey,
    string? ErrorMessage = null)
{
    public static HotkeyRegistrationResult Registered(GlobalHotkey hotkey) =>
        new(HotkeyRegistrationStatus.Registered, hotkey);

    public static HotkeyRegistrationResult AlreadyRegistered(GlobalHotkey hotkey) =>
        new(HotkeyRegistrationStatus.AlreadyRegistered, hotkey);

    public static HotkeyRegistrationResult Conflict(GlobalHotkey hotkey) =>
        new(HotkeyRegistrationStatus.Conflict, hotkey,
            "该快捷键已被其他应用或当前桌面会话占用。");

    public static HotkeyRegistrationResult Failed(GlobalHotkey hotkey, string errorMessage) =>
        new(HotkeyRegistrationStatus.Failed, hotkey, errorMessage);

    public static HotkeyRegistrationResult NoRequestedHotkey() =>
        new(HotkeyRegistrationStatus.NoRequestedHotkey, null);
}

public enum HotkeyRegistrationStatus
{
    Registered,
    AlreadyRegistered,
    Conflict,
    Failed,
    NoRequestedHotkey,
}

public sealed record HotkeyUnregistrationResult(
    HotkeyUnregistrationStatus Status,
    string? ErrorMessage = null)
{
    public bool Succeeded => Status is HotkeyUnregistrationStatus.Unregistered or
        HotkeyUnregistrationStatus.NotRegistered;

    public static HotkeyUnregistrationResult Unregistered() =>
        new(HotkeyUnregistrationStatus.Unregistered);

    public static HotkeyUnregistrationResult NotRegistered() =>
        new(HotkeyUnregistrationStatus.NotRegistered);

    public static HotkeyUnregistrationResult Failed(string errorMessage) =>
        new(HotkeyUnregistrationStatus.Failed,
            $"无法注销全局快捷键；保留当前注册状态：{errorMessage}");
}

public enum HotkeyUnregistrationStatus
{
    Unregistered,
    NotRegistered,
    Failed,
}
