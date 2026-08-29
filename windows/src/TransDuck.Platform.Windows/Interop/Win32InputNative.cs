using System.Runtime.InteropServices;

namespace TransDuck.Platform.Windows.Interop;

internal static class Win32InputNative
{
    public const uint InputKeyboard = 1;
    public const uint KeyEventKeyUp = 0x0002;
    public const int VkShift = 0x10;
    public const ushort VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const ushort VkC = 0x43;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint inputCount, NativeInput[] inputs, int inputSize);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeInput
{
    public uint Type;
    public NativeInputUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct NativeInputUnion
{
    [FieldOffset(0)]
    public NativeMouseInput Mouse;

    [FieldOffset(0)]
    public NativeKeyboardInput Keyboard;

    [FieldOffset(0)]
    public NativeHardwareInput Hardware;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeMouseInput
{
    public int X;
    public int Y;
    public uint MouseData;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeKeyboardInput
{
    public ushort VirtualKey;
    public ushort ScanCode;
    public uint Flags;
    public uint Time;
    public IntPtr ExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeHardwareInput
{
    public uint Message;
    public ushort ParameterLow;
    public ushort ParameterHigh;
}
