using Avalonia;

namespace TransDuck.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseWin32()
        .UseSkia()
        .UseHarfBuzz()
        .WithInterFont()
        .LogToTrace();
}
