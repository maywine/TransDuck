// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Platform.Windows.Startup;

/// <summary>
/// Controls the current user's opt-in sign-in registration without exposing registry details to the app layer.
/// </summary>
public interface IStartupRegistrationService : IDisposable
{
    /// <summary>Gets the state of this app's current per-user sign-in registration.</summary>
    StartupRegistrationResult GetStatus();

    /// <summary>Enables the current executable when its existing entry is absent or safely recognized as owned.</summary>
    StartupRegistrationResult Enable();

    /// <summary>Disables only an entry that is exactly current or safely recognized as owned and stale.</summary>
    StartupRegistrationResult Disable();
}

/// <summary>Defines the closed non-secret states of the per-user sign-in registration.</summary>
public enum StartupRegistrationStatus
{
    Enabled,
    Disabled,
    Stale,
    Conflict,
    Unavailable,
    Failed,
}

/// <summary>Contains only the stable result state suitable for UI and diagnostics.</summary>
public sealed record StartupRegistrationResult(StartupRegistrationStatus Status)
{
    /// <summary>Gets whether the current executable is registered exactly.</summary>
    public bool IsEnabled => Status == StartupRegistrationStatus.Enabled;

    /// <summary>Gets whether this process can safely replace or remove the existing registration.</summary>
    public bool IsOwned => Status is StartupRegistrationStatus.Enabled or StartupRegistrationStatus.Stale;

    public static StartupRegistrationResult Enabled() => new(StartupRegistrationStatus.Enabled);

    public static StartupRegistrationResult Disabled() => new(StartupRegistrationStatus.Disabled);

    public static StartupRegistrationResult Stale() => new(StartupRegistrationStatus.Stale);

    public static StartupRegistrationResult Conflict() => new(StartupRegistrationStatus.Conflict);

    public static StartupRegistrationResult Unavailable() => new(StartupRegistrationStatus.Unavailable);

    public static StartupRegistrationResult Failed() => new(StartupRegistrationStatus.Failed);
}

/// <summary>Represents the limited Run-value shapes the service needs to distinguish safely.</summary>
public enum RunRegistryValueKind
{
    Missing,
    String,
    Other,
}

/// <summary>Returns registry content without surfacing exception text or registry implementation details.</summary>
public sealed record RunRegistryValue(RunRegistryValueKind Kind, string? StringValue = null)
{
    public static RunRegistryValue Missing() => new(RunRegistryValueKind.Missing);

    public static RunRegistryValue String(string value) => new(RunRegistryValueKind.String, value);

    public static RunRegistryValue Other() => new(RunRegistryValueKind.Other);
}

/// <summary>Supplies the HKCU Run value operations so the registry boundary remains independently testable.</summary>
public interface IRunRegistryBackend : IDisposable
{
    /// <summary>Reads one named value from the current user's Run key.</summary>
    RunRegistryValue Read(string valueName);

    /// <summary>Writes one REG_SZ value to the current user's Run key.</summary>
    void WriteString(string valueName, string value);

    /// <summary>Deletes one named value from the current user's Run key if it exists.</summary>
    void Delete(string valueName);
}
