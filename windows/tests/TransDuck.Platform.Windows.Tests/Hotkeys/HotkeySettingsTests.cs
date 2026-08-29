// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Platform.Windows.Hotkeys;
using TransDuck.Platform.Windows.Interop;

namespace TransDuck.Platform.Windows.Tests.Hotkeys;

public sealed class HotkeySettingsTests
{
    [Fact]
    public void ValidateAndToGlobalHotkey_UseOnlyPersistedModifiers()
    {
        var settings = new HotkeySettings(
            HotkeySettingsMigration.CurrentVersion,
            Control: true,
            Alt: true,
            Shift: false,
            Windows: false,
            VirtualKey: 0x44);

        settings.Validate();
        var hotkey = settings.ToGlobalHotkey();

        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hotkey.Modifiers);
        Assert.Equal(0x44u, hotkey.VirtualKey);
        Assert.False(hotkey.Modifiers.HasFlag(HotkeyModifiers.NoRepeat));
    }

    [Fact]
    public void ValidateAndToGlobalHotkey_RejectInvalidVersionsModifiersAndKeys()
    {
        var invalidVersion = Settings(version: 0);
        var noModifier = Settings(control: false, alt: false, shift: false, windows: false);
        var unsupportedKey = Settings(virtualKey: 0x20);

        Assert.Throws<ContractValidationException>(invalidVersion.Validate);
        Assert.Throws<ContractValidationException>(noModifier.Validate);
        Assert.Throws<ContractValidationException>(unsupportedKey.Validate);
        Assert.Throws<ContractValidationException>(unsupportedKey.ToGlobalHotkey);
    }

    [Theory]
    [InlineData(0x70)]
    [InlineData(0x87)]
    public void ValidateAndToGlobalHotkey_AcceptsFunctionKeyBoundaries(int virtualKey)
    {
        var settings = Settings(virtualKey: (uint)virtualKey);

        settings.Validate();
        var hotkey = settings.ToGlobalHotkey();

        Assert.Equal((uint)virtualKey, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData(0x6F)]
    [InlineData(0x88)]
    public void Validate_RejectsFunctionKeysOutsideSupportedBounds(int virtualKey)
    {
        var settings = Settings(virtualKey: (uint)virtualKey);

        Assert.Throws<ContractValidationException>(settings.Validate);
    }

    [Fact]
    public void ToString_ContainsOnlySafeStructuredSettingDetails()
    {
        var description = Settings().ToString();

        Assert.Contains("HotkeySettings", description, StringComparison.Ordinal);
        Assert.Contains("Version=1", description, StringComparison.Ordinal);
        Assert.Contains("VirtualKey=0x44", description, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("query", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clipboard", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("message", description, StringComparison.OrdinalIgnoreCase);
    }

    private static HotkeySettings Settings(
        int version = HotkeySettingsMigration.CurrentVersion,
        bool control = true,
        bool alt = false,
        bool shift = false,
        bool windows = false,
        uint virtualKey = 0x44) => new(version, control, alt, shift, windows, virtualKey);
}
