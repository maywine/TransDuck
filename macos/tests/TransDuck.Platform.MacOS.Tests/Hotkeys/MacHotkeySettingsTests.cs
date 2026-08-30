using TransDuck.Core.Contracts.V1;
using TransDuck.Platform.MacOS.Hotkeys;

namespace TransDuck.Platform.MacOS.Tests.Hotkeys;

public sealed class MacHotkeySettingsTests
{
    [Fact]
    public void Default_UsesCommandOptionD()
    {
        var settings = MacHotkeySettings.Default;

        settings.Validate();
        Assert.Equal(MacHotkeySettingsMigration.CurrentVersion, settings.Version);
        Assert.Equal(MacHotkeyModifiers.Command | MacHotkeyModifiers.Option, settings.Modifiers);
        Assert.Equal(MacVirtualKey.D, settings.Key);
    }

    [Fact]
    public void Validate_RejectsMissingUnknownModifiersAndUnknownKey()
    {
        var missing = MacHotkeySettings.Default with { Modifiers = MacHotkeyModifiers.None };
        var unknownModifiers = MacHotkeySettings.Default with { Modifiers = (MacHotkeyModifiers)128 };
        var unknownKey = MacHotkeySettings.Default with { Key = (MacVirtualKey)999 };

        Assert.Throws<ContractValidationException>(missing.Validate);
        Assert.Throws<ContractValidationException>(unknownModifiers.Validate);
        Assert.Throws<ContractValidationException>(unknownKey.Validate);
    }
}
