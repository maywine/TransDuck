using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Infrastructure.Proxy;
using TransDuck.Platform.MacOS.Hotkeys;
using TransDuck.Platform.MacOS.Startup;

namespace TransDuck.MacOS.App;

internal enum ProviderCredentialKind
{
    None,
    Optional,
    ApiKey,
    VolcenginePair,
}

internal sealed record ProviderDefinition(
    string ProviderId,
    string DisplayName,
    string DefaultEndpoint,
    bool ModelRequired,
    ProviderCredentialKind CredentialKind);

internal sealed record MacRuntimeState(
    string Input,
    string Output,
    string Status,
    bool IsBusy,
    bool CanRetry);

internal sealed record MacSettingsSnapshot(
    Configuration Configuration,
    IReadOnlyList<ProviderProfileSettings> Profiles,
    ProxySettings ProxySettings,
    MacHotkeySettings HotkeySettings,
    MacStartupResult StartupResult,
    PersistenceStatus ProviderSettingsStatus,
    PersistenceStatus ConfigurationStatus,
    PersistenceStatus ProxyStatus,
    PersistenceStatus HotkeyStatus);

internal sealed record MacSettingsInput(
    string ProviderId,
    string Endpoint,
    string? Model,
    string? SourceLanguage,
    string TargetLanguage,
    int TimeoutSeconds,
    string? Credential,
    string? SecondaryCredential,
    bool ClearCredential,
    ProxySettings ProxySettings,
    MacHotkeySettings HotkeySettings,
    bool StartAtLogin,
    HistoryRetention HistoryRetention);

internal sealed record MacSettingsSaveResult(bool Succeeded, string Message);

internal sealed record RetrySnapshot(string Text, QueryKind QueryKind);
