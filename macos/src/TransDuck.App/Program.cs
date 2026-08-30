using System.Security.Cryptography;
using System.Text;
using Avalonia;

namespace TransDuck.MacOS.App;

internal static class Program
{
    internal static bool StartInBackground { get; private set; }

    internal static bool SmokeTestMode { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        SmokeTestMode = args.Contains("--smoke-test", StringComparer.Ordinal);
        StartInBackground = SmokeTestMode || args.Contains("--background", StringComparer.Ordinal);
        var avaloniaArgs = args.Where(
            static argument =>
                !string.Equals(argument, "--background", StringComparison.Ordinal) &&
                !string.Equals(argument, "--smoke-test", StringComparison.Ordinal)).ToArray();
        var identity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
        using var mutex = new Mutex(initiallyOwned: true, "TransDuck.MacOS." + identity, out var createdNew);
        if (!createdNew)
        {
            return SmokeTestMode ? 2 : 0;
        }

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(avaloniaArgs);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UseAvaloniaNative()
        .UseSkia()
        .WithInterFont()
        .LogToTrace();
}
