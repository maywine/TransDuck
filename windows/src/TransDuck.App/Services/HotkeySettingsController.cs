// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Hotkeys;
using TransDuck.Platform.Windows.Interop;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.App.Services;

/// <summary>
/// Coordinates UI-thread hotkey registration with atomic settings persistence and rollback.
/// </summary>
internal sealed class HotkeySettingsController
{
    private static readonly HotkeySettings DefaultSettings = new(
        HotkeySettingsMigration.CurrentVersion,
        Control: true,
        Alt: true,
        Shift: false,
        Windows: false,
        VirtualKey: 0x44);
    private readonly JsonHotkeySettingsStore _settingsStore;
    private readonly RegisterHotKeyService _hotkeyService;
    private readonly IDiagnosticSink _diagnosticSink;
    private HotkeySettings _currentSettings = DefaultSettings;
    private bool _isInitialized;
    private string _statusMessage = string.Empty;

    public HotkeySettingsController(
        JsonHotkeySettingsStore settingsStore,
        RegisterHotKeyService hotkeyService,
        IDiagnosticSink diagnosticSink)
    {
        _settingsStore = settingsStore;
        _hotkeyService = hotkeyService;
        _diagnosticSink = diagnosticSink;
        _statusMessage = AppStrings.Get("hotkey.status.loading");
    }

    public event EventHandler? StateChanged;

    public HotkeySettings CurrentSettings => _currentSettings;

    public bool IsInitialized => _isInitialized;

    public bool IsRegistrationActive => _hotkeyService.IsRegistered;

    public string CurrentHotkeyText => DescribeHotkey(
        _hotkeyService.RequestedHotkey ?? _currentSettings.ToGlobalHotkey());

    public string StatusMessage => _statusMessage;

    public async Task<HotkeyInitializationResult> InitializeAsync(CancellationToken cancellationToken)
    {
        PersistenceStatus readStatus;
        HotkeySettings settings;
        try
        {
            var read = await _settingsStore.ReadAsync(cancellationToken);
            readStatus = GetReadStatus(read);
            settings = readStatus == PersistenceStatus.Succeeded
                ? read.Value!
                : DefaultSettings;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readStatus = PersistenceStatus.Cancelled;
            settings = DefaultSettings;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            readStatus = PersistenceStatus.IoFailure;
            settings = DefaultSettings;
        }

        await WritePersistenceDiagnosticAsync(DiagnosticEventId.HotkeySettingsRead, readStatus);
        if (readStatus == PersistenceStatus.Cancelled || cancellationToken.IsCancellationRequested)
        {
            var cancelled = new HotkeyInitializationResult(
                PersistenceStatus.Cancelled,
                settings,
                HotkeyRegistrationResult.NoRequestedHotkey());
            SetStatus(cancelled.StatusMessage);
            return cancelled;
        }

        var registration = Register(settings);
        await WriteRegistrationDiagnosticAsync(registration);
        _currentSettings = settings;
        _isInitialized = true;

        var result = new HotkeyInitializationResult(readStatus, settings, registration);
        SetStatus(result.StatusMessage);
        return result;
    }

    public async Task<HotkeySaveResult> SaveAsync(
        HotkeySettings settings,
        CancellationToken cancellationToken)
    {
        if (!TryValidate(settings))
        {
            await WritePersistenceDiagnosticAsync(
                DiagnosticEventId.HotkeySettingsWrite,
                PersistenceStatus.InvalidData);
            var invalid = HotkeySaveResult.Invalid();
            SetStatus(invalid.StatusMessage);
            return invalid;
        }

        var previousSettings = _currentSettings;
        var previousRegistrationActive = _hotkeyService.IsRegistered;
        var registration = Register(settings);
        await WriteRegistrationDiagnosticAsync(registration);
        if (!RegistrationSucceeded(registration))
        {
            var registrationFailure = HotkeySaveResult.RegistrationFailure(
                registration.Status,
                previousRegistrationActive && !_hotkeyService.IsRegistered);
            SetStatus(registrationFailure.StatusMessage);
            return registrationFailure;
        }

        PersistenceStatus writeStatus;
        try
        {
            var write = await _settingsStore.WriteAsync(settings, cancellationToken);
            writeStatus = write.Status;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            writeStatus = PersistenceStatus.Cancelled;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            writeStatus = PersistenceStatus.IoFailure;
        }

        await WritePersistenceDiagnosticAsync(DiagnosticEventId.HotkeySettingsWrite, writeStatus);
        if (writeStatus == PersistenceStatus.Succeeded)
        {
            _currentSettings = settings;
            var completed = HotkeySaveResult.Completed();
            SetStatus(completed.StatusMessage);
            return completed;
        }

        var rollback = Register(previousSettings);
        await WriteRegistrationDiagnosticAsync(rollback);
        var persistenceFailure = RegistrationSucceeded(rollback)
            ? HotkeySaveResult.PersistenceFailureRolledBack(writeStatus)
            : HotkeySaveResult.PersistenceFailureRollbackFailed(writeStatus, rollback.Status);
        SetStatus(persistenceFailure.StatusMessage);
        return persistenceFailure;
    }

    public static bool TryCreateSettings(
        bool control,
        bool alt,
        bool shift,
        bool windows,
        string? key,
        out HotkeySettings settings)
    {
        settings = DefaultSettings;
        if (!TryParseVirtualKey(key, out var virtualKey))
        {
            return false;
        }

        var candidate = new HotkeySettings(
            HotkeySettingsMigration.CurrentVersion,
            control,
            alt,
            shift,
            windows,
            virtualKey);
        if (!TryValidate(candidate))
        {
            return false;
        }

        settings = candidate;
        return true;
    }

    public static string DescribeHotkey(HotkeySettings settings) => DescribeHotkey(settings.ToGlobalHotkey());

    public static string DescribeHotkey(GlobalHotkey hotkey)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        var parts = new List<string>(4);
        if ((hotkey.Modifiers & HotkeyModifiers.Control) != 0)
        {
            parts.Add(AppStrings.Get("hotkey.modifier.control"));
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Alt) != 0)
        {
            parts.Add(AppStrings.Get("hotkey.modifier.alt"));
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Shift) != 0)
        {
            parts.Add(AppStrings.Get("hotkey.modifier.shift"));
        }

        if ((hotkey.Modifiers & HotkeyModifiers.Windows) != 0)
        {
            parts.Add(AppStrings.Get("hotkey.modifier.windows"));
        }

        parts.Add(DescribeVirtualKey(hotkey.VirtualKey));
        return string.Join('+', parts);
    }

    private HotkeyRegistrationResult Register(HotkeySettings settings)
    {
        try
        {
            return _hotkeyService.Register(settings.ToGlobalHotkey());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return HotkeyRegistrationResult.Failed(
                settings.ToGlobalHotkey(),
                AppStrings.Get("hotkey.status.registration_failed"));
        }
    }

    private void SetStatus(string statusMessage)
    {
        _statusMessage = statusMessage;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task WritePersistenceDiagnosticAsync(
        DiagnosticEventId eventId,
        PersistenceStatus status)
    {
        await WriteDiagnosticAsync(
            eventId,
            DiagnosticLevelFor(status),
            ToDiagnosticOutcome(status),
            ToDiagnosticError(status));
    }

    private async Task WriteRegistrationDiagnosticAsync(HotkeyRegistrationResult registration)
    {
        DiagnosticErrorCode? errorCode = registration.Status switch
        {
            HotkeyRegistrationStatus.Conflict => DiagnosticErrorCode.HotkeyConflict,
            HotkeyRegistrationStatus.Registered or HotkeyRegistrationStatus.AlreadyRegistered => null,
            _ => DiagnosticErrorCode.HotkeyRegistrationFailure,
        };
        await WriteDiagnosticAsync(
            DiagnosticEventId.HotkeyRegistration,
            errorCode is null ? DiagnosticLevel.Information : DiagnosticLevel.Error,
            errorCode is null ? DiagnosticOutcome.Succeeded : DiagnosticOutcome.Failed,
            errorCode);
    }

    private async Task WriteDiagnosticAsync(
        DiagnosticEventId eventId,
        DiagnosticLevel level,
        DiagnosticOutcome outcome,
        DiagnosticErrorCode? errorCode)
    {
        try
        {
            await _diagnosticSink.WriteAsync(
                new DiagnosticEvent(
                    DateTimeOffset.UtcNow,
                    level,
                    eventId,
                    outcome,
                    ErrorCode: errorCode),
                CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Diagnostics must not change registration or persistence outcomes.
        }
    }

    private static bool TryValidate(HotkeySettings? settings)
    {
        if (settings is null)
        {
            return false;
        }

        try
        {
            settings.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }

    private static bool TryParseVirtualKey(string? key, out uint virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var trimmed = key.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.Length == 1)
        {
            var character = char.ToUpperInvariant(trimmed[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = character;
                return true;
            }
        }

        if (trimmed.Length is < 2 or > 3 || char.ToUpperInvariant(trimmed[0]) != 'F')
        {
            return false;
        }

        var functionNumber = 0;
        foreach (var digit in trimmed.AsSpan(1))
        {
            if (digit is < '0' or > '9')
            {
                return false;
            }

            functionNumber = (functionNumber * 10) + (digit - '0');
        }

        if (functionNumber is >= 1 and <= 24)
        {
            virtualKey = 0x70u + (uint)(functionNumber - 1);
            return true;
        }

        return false;
    }

    private static PersistenceStatus GetReadStatus<TValue>(PersistenceReadResult<TValue> result)
        where TValue : class =>
        result.Status == PersistenceStatus.Succeeded && result.Value is null
            ? PersistenceStatus.InvalidData
            : result.Status;

    private static bool RegistrationSucceeded(HotkeyRegistrationResult registration) =>
        registration.Status is HotkeyRegistrationStatus.Registered or HotkeyRegistrationStatus.AlreadyRegistered;

    private static DiagnosticLevel DiagnosticLevelFor(PersistenceStatus status) => status switch
    {
        PersistenceStatus.InvalidData or
        PersistenceStatus.UnsupportedVersion or
        PersistenceStatus.CorruptData or
        PersistenceStatus.IoFailure => DiagnosticLevel.Error,
        _ => DiagnosticLevel.Information,
    };

    private static DiagnosticOutcome ToDiagnosticOutcome(PersistenceStatus status) => status switch
    {
        PersistenceStatus.Succeeded => DiagnosticOutcome.Succeeded,
        PersistenceStatus.NotFound => DiagnosticOutcome.NotFound,
        PersistenceStatus.Cancelled => DiagnosticOutcome.Cancelled,
        _ => DiagnosticOutcome.Failed,
    };

    private static DiagnosticErrorCode? ToDiagnosticError(PersistenceStatus status) => status switch
    {
        PersistenceStatus.InvalidData => DiagnosticErrorCode.InvalidData,
        PersistenceStatus.UnsupportedVersion => DiagnosticErrorCode.UnsupportedVersion,
        PersistenceStatus.CorruptData => DiagnosticErrorCode.CorruptData,
        PersistenceStatus.IoFailure => DiagnosticErrorCode.IoFailure,
        _ => null,
    };

    public static string DescribeVirtualKey(uint virtualKey) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x70 + 1}",
        _ => AppStrings.Format("hotkey.key.virtual", virtualKey),
    };
}

/// <summary>
/// Describes startup selection, including a safe fallback when persisted settings are unavailable.
/// </summary>
internal sealed record HotkeyInitializationResult(
    PersistenceStatus ReadStatus,
    HotkeySettings Settings,
    HotkeyRegistrationResult Registration)
{
    public string StatusMessage
    {
        get
        {
            var settingsStatus = ReadStatus switch
            {
                PersistenceStatus.Succeeded => AppStrings.Get("hotkey.status.loaded"),
                PersistenceStatus.NotFound => AppStrings.Get("hotkey.status.not_found"),
                PersistenceStatus.Cancelled => AppStrings.Get("hotkey.status.cancelled"),
                _ => AppStrings.Get("hotkey.status.fallback"),
            };
            return Registration.Status switch
            {
                HotkeyRegistrationStatus.Conflict => ReadStatus switch
                {
                    PersistenceStatus.Succeeded => AppStrings.Get("hotkey.status.loaded_conflict"),
                    PersistenceStatus.NotFound => AppStrings.Get("hotkey.status.not_found_conflict"),
                    _ => AppStrings.Get("hotkey.status.fallback_conflict"),
                },
                HotkeyRegistrationStatus.Failed => ReadStatus switch
                {
                    PersistenceStatus.Succeeded => AppStrings.Get("hotkey.status.loaded_registration_failed"),
                    PersistenceStatus.NotFound => AppStrings.Get("hotkey.status.not_found_registration_failed"),
                    _ => AppStrings.Get("hotkey.status.fallback_registration_failed"),
                },
                _ => settingsStatus,
            };
        }
    }
}

/// <summary>
/// Describes the registration, persistence, and compensation stages of a user-initiated save.
/// </summary>
internal sealed record HotkeySaveResult(
    HotkeySaveStage Stage,
    PersistenceStatus? PersistenceStatus = null,
    HotkeyRegistrationStatus? RegistrationStatus = null)
{
    public bool Succeeded => Stage == HotkeySaveStage.Completed;

    public string StatusMessage => Stage switch
    {
        HotkeySaveStage.Completed => AppStrings.Get("hotkey.save.completed"),
        HotkeySaveStage.Invalid => AppStrings.Get("hotkey.save.invalid"),
        HotkeySaveStage.RegistrationConflict => AppStrings.Get("hotkey.save.conflict"),
        HotkeySaveStage.RegistrationFailed => AppStrings.Get("hotkey.save.registration_failed"),
        HotkeySaveStage.RegistrationFailureRollbackFailed => AppStrings.Get("hotkey.save.registration_rollback_failed"),
        HotkeySaveStage.PersistenceFailureRolledBack => AppStrings.Get("hotkey.save.persistence_rolled_back"),
        HotkeySaveStage.PersistenceFailureRollbackFailed => AppStrings.Get("hotkey.save.persistence_rollback_failed"),
        _ => AppStrings.Get("hotkey.save.incomplete"),
    };

    public static HotkeySaveResult Completed() => new(HotkeySaveStage.Completed);

    public static HotkeySaveResult Invalid() => new(HotkeySaveStage.Invalid);

    public static HotkeySaveResult RegistrationFailure(
        HotkeyRegistrationStatus status,
        bool rollbackFailed) => new(
        rollbackFailed
            ? HotkeySaveStage.RegistrationFailureRollbackFailed
            : status == HotkeyRegistrationStatus.Conflict
                ? HotkeySaveStage.RegistrationConflict
                : HotkeySaveStage.RegistrationFailed,
        RegistrationStatus: status);

    public static HotkeySaveResult PersistenceFailureRolledBack(PersistenceStatus status) => new(
        HotkeySaveStage.PersistenceFailureRolledBack,
        PersistenceStatus: status);

    public static HotkeySaveResult PersistenceFailureRollbackFailed(
        PersistenceStatus persistenceStatus,
        HotkeyRegistrationStatus rollbackStatus) => new(
        HotkeySaveStage.PersistenceFailureRollbackFailed,
        persistenceStatus,
        rollbackStatus);
}

internal enum HotkeySaveStage
{
    Completed,
    Invalid,
    RegistrationConflict,
    RegistrationFailed,
    RegistrationFailureRollbackFailed,
    PersistenceFailureRolledBack,
    PersistenceFailureRollbackFailed,
}
