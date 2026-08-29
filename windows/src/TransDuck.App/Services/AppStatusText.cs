// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Platform.Windows.Capture;
using TransDuck.Platform.Windows.Clipboard;
using TransDuck.Platform.Windows.Hotkeys;
using TransDuck.Platform.Windows.Ocr;
using TransDuck.Platform.Windows.Selection;
using TransDuck.Platform.Windows.Tray;

namespace TransDuck.App.Services;

/// <summary>
/// Maps stable App and Platform outcomes to complete localized user-facing messages.
/// </summary>
internal static class AppStatusText
{
    public static string DescribeTranslationSettingsFailure(ProviderTranslationSettingsStatus status) => status switch
    {
        ProviderTranslationSettingsStatus.ProviderSettingsNotFound => AppStrings.Get("translation.settings.provider_not_found"),
        ProviderTranslationSettingsStatus.ConfigurationNotFound => AppStrings.Get("translation.settings.configuration_not_found"),
        ProviderTranslationSettingsStatus.ProfileNotFound => AppStrings.Get("translation.settings.profile_not_found"),
        ProviderTranslationSettingsStatus.CredentialUnavailable => AppStrings.Get("translation.settings.credential_unavailable"),
        _ => AppStrings.Get("translation.settings.unavailable"),
    };

    public static string DescribeTranslationFailure(QueryErrorCode errorCode) =>
        AppStrings.DescribeQueryError(errorCode);

    public static string DescribeTranslationErrorCode(QueryErrorCode errorCode) =>
        AppStrings.DescribeQueryErrorCode(errorCode);

    public static string DescribeCaptureStatus(ScreenCaptureStatus status) => status switch
    {
        ScreenCaptureStatus.NotSupported => AppStrings.Get("capture.status.not_supported"),
        ScreenCaptureStatus.Cancelled => AppStrings.Get("capture.status.cancelled"),
        _ => AppStrings.Get("capture.status.failed"),
    };

    public static string DescribeOcrStatus(WindowsOcrStatus status) => status switch
    {
        WindowsOcrStatus.Succeeded => AppStrings.Get("ocr.status.completed"),
        WindowsOcrStatus.PackageIdentityRequired => AppStrings.Get("ocr.status.package_identity_required"),
        WindowsOcrStatus.LanguageUnavailable => AppStrings.Get("ocr.status.language_unavailable"),
        WindowsOcrStatus.ImageTooLarge => AppStrings.Get("ocr.status.image_too_large"),
        WindowsOcrStatus.Cancelled => AppStrings.Get("ocr.status.cancelled"),
        _ => AppStrings.Get("ocr.status.failed"),
    };

    public static string DescribeHotkeyResult(HotkeyRegistrationResult result)
    {
        var hotkeyText = result.Hotkey is { } hotkey
            ? HotkeySettingsController.DescribeHotkey(hotkey)
            : AppStrings.Get("hotkey.result.unknown");
        return result.Status switch
        {
            HotkeyRegistrationStatus.Registered => AppStrings.Format("hotkey.result.registered", hotkeyText),
            HotkeyRegistrationStatus.AlreadyRegistered => AppStrings.Format("hotkey.result.already_registered", hotkeyText),
            HotkeyRegistrationStatus.Conflict => AppStrings.Format("hotkey.result.conflict", hotkeyText),
            HotkeyRegistrationStatus.Failed => AppStrings.Format("hotkey.result.failed", hotkeyText),
            _ => AppStrings.Get("hotkey.result.none"),
        };
    }

    public static string DescribeTrayStartResult(TrayOperationResult result) => result.Succeeded
        ? AppStrings.Get("runtime.tray.started")
        : AppStrings.Get("runtime.tray.failed");

    public static string DescribeExplorerRestartResult(TrayOperationResult result) => result.Succeeded
        ? AppStrings.Get("runtime.tray.explorer_restarted")
        : AppStrings.Get("runtime.tray.explorer_failed");

    public static string DescribeSelectionSuccess(SelectionReadResult selection)
    {
        var formatWarning = selection.UnsupportedClipboardFormats.Count == 0
            ? string.Empty
            : AppStrings.Format(
                "selection.clipboard.unsupported_formats",
                string.Join(", ", selection.UnsupportedClipboardFormats));
        var restoration = DescribeClipboardRestoration(selection.ClipboardRestoration);
        return AppStrings.Format(
            "selection.success",
            DescribeSelectionPath(selection.Path),
            string.Join(' ', new[] { formatWarning, restoration }.Where(text => !string.IsNullOrEmpty(text))));
    }

    public static string DescribeSelectionFailure(SelectionReadResult selection)
    {
        var failure = selection.Status switch
        {
            SelectionReadStatus.FallbackRequired => AppStrings.Get("selection.failure.fallback_required"),
            SelectionReadStatus.Cancelled => AppStrings.Get("selection.failure.cancelled"),
            _ => AppStrings.Get("selection.failure.failed"),
        };
        var restoration = DescribeClipboardRestoration(selection.ClipboardRestoration);
        return string.IsNullOrEmpty(restoration)
            ? failure
            : AppStrings.Format("selection.failure.with_restoration", failure, restoration);
    }

    private static string DescribeSelectionPath(SelectionReadPath path) => path switch
    {
        SelectionReadPath.TextPattern => AppStrings.Get("selection.path.text_pattern"),
        SelectionReadPath.ClipboardCopy => AppStrings.Get("selection.path.clipboard_copy"),
        _ => AppStrings.Get("selection.path.clipboard_copy"),
    };

    private static string DescribeClipboardRestoration(ClipboardRestorationResult restoration) =>
        restoration.Status switch
        {
            ClipboardRestorationStatus.Restored => AppStrings.Get("selection.clipboard.restored"),
            ClipboardRestorationStatus.SkippedForConcurrentChange => AppStrings.Get("selection.clipboard.concurrent_change"),
            ClipboardRestorationStatus.Failed => AppStrings.Get("selection.clipboard.restore_failed"),
            _ => string.Empty,
        };
}
