// Copyright (c) 2026 maywine. All rights reserved.

using Microsoft.Win32;
using System.IO;

namespace TransDuck.Platform.Windows.Startup;

/// <summary>
/// Registers the self-contained ZIP executable in HKCU Run. It refuses unknown values so a moved ZIP cannot
/// accidentally replace or delete another application's entry that happens to share the value name.
/// </summary>
public sealed class RegistryRunStartupRegistrationService : IStartupRegistrationService
{
    /// <summary>The fixed value name owned by TransDuck in HKCU Run.</summary>
    public const string ValueName = "TransDuck.Windows";

    private readonly IRunRegistryBackend _registry;
    private readonly Func<string?> _currentProcessPathProvider;
    private bool _disposed;

    /// <summary>Creates the HKCU Run service with injectable storage and process-path providers.</summary>
    public RegistryRunStartupRegistrationService(
        IRunRegistryBackend? registry = null,
        Func<string?>? currentProcessPathProvider = null)
    {
        _registry = registry ?? new CurrentUserRunRegistryBackend();
        _currentProcessPathProvider = currentProcessPathProvider ?? (() => Environment.ProcessPath);
    }

    /// <inheritdoc />
    public StartupRegistrationResult GetStatus()
    {
        if (!TryCreateCurrentCommand(out var command))
        {
            return StartupRegistrationResult.Unavailable();
        }

        try
        {
            return Classify(_registry.Read(ValueName), command);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return StartupRegistrationResult.Failed();
        }
    }

    /// <inheritdoc />
    public StartupRegistrationResult Enable()
    {
        if (!TryCreateCurrentCommand(out var command))
        {
            return StartupRegistrationResult.Unavailable();
        }

        try
        {
            var existing = Classify(_registry.Read(ValueName), command);
            switch (existing.Status)
            {
                case StartupRegistrationStatus.Enabled:
                    return existing;
                case StartupRegistrationStatus.Disabled:
                case StartupRegistrationStatus.Stale:
                    _registry.WriteString(ValueName, command);
                    return StartupRegistrationResult.Enabled();
                default:
                    return existing;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return StartupRegistrationResult.Failed();
        }
    }

    /// <inheritdoc />
    public StartupRegistrationResult Disable()
    {
        if (!TryCreateCurrentCommand(out var command))
        {
            return StartupRegistrationResult.Unavailable();
        }

        try
        {
            var existing = Classify(_registry.Read(ValueName), command);
            if (existing.IsOwned)
            {
                _registry.Delete(ValueName);
                return StartupRegistrationResult.Disabled();
            }

            return existing;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return StartupRegistrationResult.Failed();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registry.Dispose();
    }

    private bool TryCreateCurrentCommand(out string command)
    {
        command = string.Empty;
        if (_disposed)
        {
            return false;
        }

        var currentProcessPath = _currentProcessPathProvider();
        if (string.IsNullOrWhiteSpace(currentProcessPath))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(currentProcessPath))
            {
                return false;
            }

            var executablePath = Path.GetFullPath(currentProcessPath);
            if (!File.Exists(executablePath) ||
                !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(executablePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(executablePath), "TransDuck.exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            command = Quote(executablePath);
            return command.Length <= 260;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            command = string.Empty;
            return false;
        }
    }

    private static StartupRegistrationResult Classify(RunRegistryValue value, string currentCommand) => value.Kind switch
    {
        RunRegistryValueKind.Missing => StartupRegistrationResult.Disabled(),
        RunRegistryValueKind.String when string.Equals(value.StringValue, currentCommand, StringComparison.Ordinal) =>
            StartupRegistrationResult.Enabled(),
        RunRegistryValueKind.String when IsOwnedStaleCommand(value.StringValue) => StartupRegistrationResult.Stale(),
        _ => StartupRegistrationResult.Conflict(),
    };

    private static bool IsOwnedStaleCommand(string? command)
    {
        if (string.IsNullOrEmpty(command) || command.Length < 3 || command[0] != '"' || command[^1] != '"' ||
            command.IndexOf('"', 1) != command.Length - 1)
        {
            return false;
        }

        var executablePath = command[1..^1];
        try
        {
            return Path.IsPathFullyQualified(executablePath) &&
                string.Equals(Path.GetFileName(executablePath), "TransDuck.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }

    private static string Quote(string executablePath) => '"' + executablePath + '"';

    private sealed class CurrentUserRunRegistryBackend : IRunRegistryBackend
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public RunRegistryValue Read(string valueName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key is null)
            {
                return RunRegistryValue.Missing();
            }

            var value = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null)
            {
                return RunRegistryValue.Missing();
            }

            if (key.GetValueKind(valueName) != RegistryValueKind.String)
            {
                return RunRegistryValue.Other();
            }

            return value switch
            {
                string stringValue => RunRegistryValue.String(stringValue),
                _ => RunRegistryValue.Other(),
            };
        }

        public void WriteString(string valueName, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Current-user Run key is unavailable.");
            key.SetValue(valueName, value, RegistryValueKind.String);
        }

        public void Delete(string valueName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(valueName, throwOnMissingValue: false);
        }

        public void Dispose()
        {
        }
    }
}
