using TransDuck.Infrastructure.Persistence;

namespace TransDuck.Platform.MacOS.Persistence;

/// <summary>
/// Resolves current-user macOS application data locations without creating them.
/// </summary>
public sealed class MacDataPaths : IApplicationDataPaths
{
    public MacDataPaths(string? rootDirectory = null, string? homeDirectory = null)
    {
        if (rootDirectory is null)
        {
            homeDirectory ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(homeDirectory))
            {
                throw new InvalidOperationException("The current macOS user home directory is unavailable.");
            }

            rootDirectory = Path.Combine(homeDirectory, "Library", "Application Support", "TransDuck");
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public string RootDirectory { get; }

    public string ConfigurationFilePath => Path.Combine(RootDirectory, "configuration.v1.json");

    public string ProviderSettingsFilePath => Path.Combine(RootDirectory, "provider-settings.v1.json");

    public string ProxySettingsFilePath => Path.Combine(RootDirectory, "proxy-settings.v1.json");

    public string HotkeySettingsFilePath => Path.Combine(RootDirectory, "hotkey-settings.v1.json");

    public string HistoryFilePath => Path.Combine(RootDirectory, "history.v1.jsonl");

    public string DiagnosticFilePath => Path.Combine(RootDirectory, "diagnostics.v1.jsonl");

    public void EnsureRootDirectory()
    {
        Directory.CreateDirectory(RootDirectory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                RootDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
