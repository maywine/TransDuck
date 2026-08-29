// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;
using TransDuck.Core.Contracts.V1;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Hotkeys;

/// <summary>
/// Defines the latest persisted hotkey settings version supported by this client.
/// </summary>
public static class HotkeySettingsMigration
{
    /// <summary>Gets the only hotkey settings Version accepted by this implementation.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Stores a user-selected global hotkey without persisting transient Win32 registration flags.
/// </summary>
public sealed record HotkeySettings(
    [property: JsonRequired] int Version,
    [property: JsonRequired] bool Control,
    [property: JsonRequired] bool Alt,
    [property: JsonRequired] bool Shift,
    [property: JsonRequired] bool Windows,
    [property: JsonRequired] uint VirtualKey)
{
    private const uint DigitVirtualKeyFirst = 0x30;
    private const uint DigitVirtualKeyLast = 0x39;
    private const uint LetterVirtualKeyFirst = 0x41;
    private const uint LetterVirtualKeyLast = 0x5A;
    private const uint FunctionVirtualKeyFirst = 0x70;
    private const uint FunctionVirtualKeyLast = 0x87;

    /// <summary>Validates the version, modifier combination, and supported virtual-key range.</summary>
    public void Validate()
    {
        if (Version < 1)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "version must be positive.");
        }

        if (!Control && !Alt && !Shift && !Windows)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "At least one hotkey modifier is required.");
        }

        if (!IsSupportedVirtualKey(VirtualKey))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "virtualKey must be A-Z, 0-9, or F1-F24.");
        }
    }

    /// <summary>Creates the requested global hotkey without the transient NoRepeat registration flag.</summary>
    public GlobalHotkey ToGlobalHotkey()
    {
        Validate();
        var modifiers = (HotkeyModifiers)0;
        if (Control)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (Alt)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Shift)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Windows)
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return new GlobalHotkey(modifiers, VirtualKey);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"HotkeySettings(Version={Version}, Control={Control}, Alt={Alt}, Shift={Shift}, " +
        $"Windows={Windows}, VirtualKey=0x{VirtualKey:X2})";

    private static bool IsSupportedVirtualKey(uint virtualKey) =>
        virtualKey is >= DigitVirtualKeyFirst and <= DigitVirtualKeyLast ||
        virtualKey is >= LetterVirtualKeyFirst and <= LetterVirtualKeyLast ||
        virtualKey is >= FunctionVirtualKeyFirst and <= FunctionVirtualKeyLast;
}
