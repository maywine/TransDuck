using System.Runtime.InteropServices;

namespace TransDuck.App.Services;

internal static class NativeMessageBox
{
    private const uint Ok = 0x00000000;
    private const uint YesNo = 0x00000004;
    private const uint IconError = 0x00000010;
    private const uint IconWarning = 0x00000030;
    private const uint DefaultButton2 = 0x00000100;
    private const int Yes = 6;

    public static void ShowError(IntPtr owner, string message, string title) =>
        MessageBox(owner, message, title, Ok | IconError);

    public static bool Confirm(IntPtr owner, string message, string title) =>
        MessageBox(owner, message, title, YesNo | IconWarning | DefaultButton2) == Yes;

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
