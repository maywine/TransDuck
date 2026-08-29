// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;

namespace TransDuck.Platform.Windows.Persistence;

/// <summary>
/// Resolves Windows persistence paths without creating files or directories in its constructor.
/// </summary>
public sealed class WindowsDataPaths
{
    /// <summary>Creates paths rooted at an injected directory or %LocalAppData%\TransDuck.</summary>
    public WindowsDataPaths(string? rootDirectory = null)
    {
        if (rootDirectory is null)
        {
            var localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException("Windows LocalAppData is unavailable.");
            }

            rootDirectory = Path.Combine(localApplicationData, "TransDuck");
        }

        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>Gets the root directory used by all Windows persistence stores.</summary>
    public string RootDirectory { get; }

    /// <summary>Gets the v1 configuration file path.</summary>
    public string ConfigurationFilePath => Path.Combine(RootDirectory, "configuration.v1.json");

    /// <summary>Gets the non-secret provider profile settings file path.</summary>
    public string ProviderSettingsFilePath => Path.Combine(RootDirectory, "provider-settings.v1.json");

    /// <summary>Gets the non-secret global hotkey settings file path.</summary>
    public string HotkeySettingsFilePath => Path.Combine(RootDirectory, "hotkey-settings.v1.json");

    /// <summary>Gets the directory used for DPAPI credential envelopes.</summary>
    public string CredentialsDirectoryPath => Path.Combine(RootDirectory, "credentials");

    /// <summary>Gets the JSON Lines history file path.</summary>
    public string HistoryFilePath => Path.Combine(RootDirectory, "history.v1.jsonl");

    /// <summary>Gets the JSON Lines diagnostic file path.</summary>
    public string DiagnosticFilePath => Path.Combine(RootDirectory, "diagnostics.v1.jsonl");
}
