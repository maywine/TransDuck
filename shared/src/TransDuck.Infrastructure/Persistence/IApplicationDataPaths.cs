namespace TransDuck.Infrastructure.Persistence;

/// <summary>
/// Resolves platform-owned non-secret persistence locations without creating them.
/// </summary>
public interface IApplicationDataPaths
{
    string RootDirectory { get; }

    string ConfigurationFilePath { get; }

    string ProviderSettingsFilePath { get; }

    string ProxySettingsFilePath { get; }

    string HistoryFilePath { get; }

    string DiagnosticFilePath { get; }
}
