using System.Security.Cryptography;
using System.Text;
using Avalonia;
using TransDuck.Core.Lookup;
using TransDuck.Platform.MacOS.Dictionary;

namespace TransDuck.MacOS.App;

internal static class Program
{
    internal static bool StartInBackground { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--smoke-test", StringComparer.Ordinal))
        {
            return RunSmokeTest();
        }

        StartInBackground = args.Contains("--background", StringComparer.Ordinal);
        var avaloniaArgs = args.Where(
            static argument => !string.Equals(argument, "--background", StringComparison.Ordinal)).ToArray();
        var identity = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
        using var mutex = new Mutex(initiallyOwned: true, "TransDuck.MacOS." + identity, out var createdNew);
        if (!createdNew)
        {
            return 0;
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

    private static int RunSmokeTest()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return 1;
        }

        try
        {
            var result = new MacSystemDictionaryProvider().LookupAsync(
                "dictionary",
                dataFilePath: null,
                CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            return result.Status is DictionaryLookupStatus.Found or DictionaryLookupStatus.NotFound
                ? 0
                : 1;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return 1;
        }
    }
}
