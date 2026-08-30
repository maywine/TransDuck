using System.Runtime.InteropServices;
using TransDuck.Platform.MacOS.Interop;

namespace TransDuck.Platform.MacOS.Selection;

public enum MacSelectionStatus
{
    Succeeded,
    PermissionRequired,
    NoFocusedElement,
    NoSelection,
    Unsupported,
    Failed,
}

public sealed record MacSelectionResult(MacSelectionStatus Status, string? Text = null)
{
    public bool Succeeded => Status == MacSelectionStatus.Succeeded && !string.IsNullOrEmpty(Text);
}

public enum MacAccessibilityReadStatus
{
    Succeeded,
    NoFocusedElement,
    NoValue,
    Unsupported,
    Failed,
}

public sealed record MacAccessibilityReadResult(
    MacAccessibilityReadStatus Status,
    string? Text = null);

public interface IMacAccessibilityBackend
{
    bool IsProcessTrusted(bool prompt);

    MacAccessibilityReadResult ReadSelectedText();
}

public sealed class MacAccessibilitySelectionService
{
    private readonly IMacAccessibilityBackend _backend;

    public MacAccessibilitySelectionService()
        : this(new ApplicationServicesAccessibilityBackend())
    {
    }

    public MacAccessibilitySelectionService(IMacAccessibilityBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public bool EnsurePermission(bool prompt)
    {
        try
        {
            return _backend.IsProcessTrusted(prompt);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return false;
        }
    }

    public MacSelectionResult ReadSelectedText(bool promptForPermission = false)
    {
        try
        {
            if (!_backend.IsProcessTrusted(promptForPermission))
            {
                return new MacSelectionResult(MacSelectionStatus.PermissionRequired);
            }

            var result = _backend.ReadSelectedText();
            if (result.Status == MacAccessibilityReadStatus.Succeeded)
            {
                return string.IsNullOrWhiteSpace(result.Text)
                    ? new MacSelectionResult(MacSelectionStatus.NoSelection)
                    : new MacSelectionResult(MacSelectionStatus.Succeeded, result.Text);
            }

            return new MacSelectionResult(result.Status switch
            {
                MacAccessibilityReadStatus.NoFocusedElement => MacSelectionStatus.NoFocusedElement,
                MacAccessibilityReadStatus.NoValue => MacSelectionStatus.NoSelection,
                MacAccessibilityReadStatus.Unsupported => MacSelectionStatus.Unsupported,
                _ => MacSelectionStatus.Failed,
            });
        }
        catch (PlatformNotSupportedException)
        {
            return new MacSelectionResult(MacSelectionStatus.Unsupported);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return new MacSelectionResult(MacSelectionStatus.Failed);
        }
    }
}

internal sealed partial class ApplicationServicesAccessibilityBackend : IMacAccessibilityBackend
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const int Success = 0;
    private const int AttributeUnsupported = -25205;
    private const int NoValue = -25212;

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrusted();

    [LibraryImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool AXIsProcessTrustedWithOptions(IntPtr options);

    [LibraryImport(ApplicationServices)]
    private static partial IntPtr AXUIElementCreateSystemWide();

    [LibraryImport(ApplicationServices)]
    private static partial int AXUIElementCopyAttributeValue(
        IntPtr element,
        IntPtr attribute,
        out IntPtr value);

    public bool IsProcessTrusted(bool prompt)
    {
        EnsureMacOS();
        if (!prompt)
        {
            return AXIsProcessTrusted();
        }

        using var scope = new CoreFoundationScope();
        var options = scope.Dictionary(new KeyValuePair<IntPtr, IntPtr>(
            scope.String("AXTrustedCheckOptionPrompt"),
            CoreFoundationSymbols.BooleanTrue));
        return AXIsProcessTrustedWithOptions(options);
    }

    public MacAccessibilityReadResult ReadSelectedText()
    {
        EnsureMacOS();
        using var scope = new CoreFoundationScope();
        var systemWide = scope.Own(AXUIElementCreateSystemWide());
        var focusedStatus = AXUIElementCopyAttributeValue(
            systemWide,
            scope.String("AXFocusedUIElement"),
            out var focused);
        if (focusedStatus != Success || focused == IntPtr.Zero)
        {
            return new MacAccessibilityReadResult(focusedStatus switch
            {
                AttributeUnsupported => MacAccessibilityReadStatus.Unsupported,
                NoValue => MacAccessibilityReadStatus.NoFocusedElement,
                _ => MacAccessibilityReadStatus.Failed,
            });
        }

        scope.Own(focused);
        var selectionStatus = AXUIElementCopyAttributeValue(
            focused,
            scope.String("AXSelectedText"),
            out var selectedText);
        if (selectionStatus != Success || selectedText == IntPtr.Zero)
        {
            return new MacAccessibilityReadResult(selectionStatus switch
            {
                AttributeUnsupported => MacAccessibilityReadStatus.Unsupported,
                NoValue => MacAccessibilityReadStatus.NoValue,
                _ => MacAccessibilityReadStatus.Failed,
            });
        }

        scope.Own(selectedText);
        var text = CoreFoundationNative.CopyString(selectedText);
        return text is null
            ? new MacAccessibilityReadResult(MacAccessibilityReadStatus.Failed)
            : new MacAccessibilityReadResult(MacAccessibilityReadStatus.Succeeded, text);
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS Accessibility is only available on macOS.");
        }
    }

    private static class CoreFoundationSymbols
    {
        private const string CoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private static readonly IntPtr Handle = NativeLibrary.Load(CoreFoundation);

        internal static readonly IntPtr BooleanTrue =
            Marshal.ReadIntPtr(NativeLibrary.GetExport(Handle, "kCFBooleanTrue"));
    }
}
