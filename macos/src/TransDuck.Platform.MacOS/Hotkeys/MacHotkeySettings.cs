using System.Text.Json.Serialization;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Platform.MacOS.Hotkeys;

public static class MacHotkeySettingsMigration
{
    public const int CurrentVersion = 1;
}

[Flags]
public enum MacHotkeyModifiers
{
    None = 0,
    Control = 1,
    Option = 2,
    Shift = 4,
    Command = 8,
}

public enum MacVirtualKey
{
    A,
    B,
    C,
    D,
    E,
    F,
    G,
    H,
    I,
    J,
    K,
    L,
    M,
    N,
    O,
    P,
    Q,
    R,
    S,
    T,
    U,
    V,
    W,
    X,
    Y,
    Z,
    Digit0,
    Digit1,
    Digit2,
    Digit3,
    Digit4,
    Digit5,
    Digit6,
    Digit7,
    Digit8,
    Digit9,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
    F13,
    F14,
    F15,
    F16,
    F17,
    F18,
    F19,
    F20,
    F21,
    F22,
    F23,
    F24,
}

public sealed record MacHotkeySettings(
    [property: JsonRequired] int Version,
    [property: JsonRequired] MacHotkeyModifiers Modifiers,
    [property: JsonRequired] MacVirtualKey Key)
{
    private const MacHotkeyModifiers AllModifiers =
        MacHotkeyModifiers.Control |
        MacHotkeyModifiers.Option |
        MacHotkeyModifiers.Shift |
        MacHotkeyModifiers.Command;

    public static MacHotkeySettings Default { get; } = new(
        MacHotkeySettingsMigration.CurrentVersion,
        MacHotkeyModifiers.Command | MacHotkeyModifiers.Option,
        MacVirtualKey.D);

    public void Validate()
    {
        if (Version != MacHotkeySettingsMigration.CurrentVersion)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "macOS hotkey settings version is not supported.");
        }

        if (Modifiers == MacHotkeyModifiers.None || (Modifiers & ~AllModifiers) != 0)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "A supported macOS hotkey modifier is required.");
        }

        if (!Enum.IsDefined(Key))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "The macOS hotkey key is not supported.");
        }
    }

    public override string ToString() => $"MacHotkeySettings(Version={Version}, Modifiers={Modifiers}, Key={Key})";
}
