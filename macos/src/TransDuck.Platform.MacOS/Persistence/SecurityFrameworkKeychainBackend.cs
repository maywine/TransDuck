using System.Runtime.InteropServices;
using TransDuck.Platform.MacOS.Interop;

namespace TransDuck.Platform.MacOS.Persistence;

internal sealed partial class SecurityFrameworkKeychainBackend : IMacKeychainBackend
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const int Success = 0;
    private const int DuplicateItem = -25299;
    private const int ItemNotFound = -25300;
    private const int AuthFailed = -25293;
    private const int UserCanceled = -128;

    [LibraryImport(Security)]
    private static partial int SecItemCopyMatching(IntPtr query, out IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemAdd(IntPtr attributes, IntPtr result);

    [LibraryImport(Security)]
    private static partial int SecItemUpdate(IntPtr query, IntPtr attributesToUpdate);

    [LibraryImport(Security)]
    private static partial int SecItemDelete(IntPtr query);

    public MacKeychainReadResult Get(string service, string account)
    {
        EnsureMacOS();
        using var scope = new CoreFoundationScope();
        var query = CreateIdentityQuery(scope, service, account, includeReturnData: true);
        var status = SecItemCopyMatching(query, out var result);
        if (status != Success || result == IntPtr.Zero)
        {
            return new MacKeychainReadResult(Map(status));
        }

        try
        {
            var value = CoreFoundationNative.CopyData(result);
            return value.Length == 0
                ? new MacKeychainReadResult(MacKeychainBackendStatus.Failed)
                : new MacKeychainReadResult(MacKeychainBackendStatus.Succeeded, value);
        }
        finally
        {
            CoreFoundationNative.CFRelease(result);
        }
    }

    public MacKeychainBackendStatus Set(string service, string account, ReadOnlySpan<byte> value)
    {
        EnsureMacOS();
        using var scope = new CoreFoundationScope();
        var identity = CreateIdentityQuery(scope, service, account, includeReturnData: false);
        var data = scope.Data(value);
        var update = scope.Dictionary(Pair(SecuritySymbols.ValueData, data));
        var updateStatus = SecItemUpdate(identity, update);
        if (updateStatus == Success)
        {
            return MacKeychainBackendStatus.Succeeded;
        }

        if (updateStatus != ItemNotFound)
        {
            return Map(updateStatus);
        }

        var add = scope.Dictionary(
            Pair(SecuritySymbols.Class, SecuritySymbols.ClassGenericPassword),
            Pair(SecuritySymbols.AttributeService, scope.String(service)),
            Pair(SecuritySymbols.AttributeAccount, scope.String(account)),
            Pair(SecuritySymbols.ValueData, data));
        var addStatus = SecItemAdd(add, IntPtr.Zero);
        if (addStatus == DuplicateItem)
        {
            addStatus = SecItemUpdate(identity, update);
        }

        return Map(addStatus);
    }

    public MacKeychainBackendStatus Remove(string service, string account)
    {
        EnsureMacOS();
        using var scope = new CoreFoundationScope();
        return Map(SecItemDelete(CreateIdentityQuery(scope, service, account, includeReturnData: false)));
    }

    public void Dispose()
    {
    }

    private static IntPtr CreateIdentityQuery(
        CoreFoundationScope scope,
        string service,
        string account,
        bool includeReturnData)
    {
        var entries = new List<KeyValuePair<IntPtr, IntPtr>>
        {
            Pair(SecuritySymbols.Class, SecuritySymbols.ClassGenericPassword),
            Pair(SecuritySymbols.AttributeService, scope.String(service)),
            Pair(SecuritySymbols.AttributeAccount, scope.String(account)),
        };
        if (includeReturnData)
        {
            entries.Add(Pair(SecuritySymbols.ReturnData, SecuritySymbols.BooleanTrue));
            entries.Add(Pair(SecuritySymbols.MatchLimit, SecuritySymbols.MatchLimitOne));
        }

        return scope.Own(CoreFoundationNative.CreateDictionary(entries));
    }

    private static KeyValuePair<IntPtr, IntPtr> Pair(IntPtr key, IntPtr value) => new(key, value);

    private static MacKeychainBackendStatus Map(int status) => status switch
    {
        Success => MacKeychainBackendStatus.Succeeded,
        ItemNotFound => MacKeychainBackendStatus.NotFound,
        AuthFailed or UserCanceled => MacKeychainBackendStatus.Denied,
        _ => MacKeychainBackendStatus.Failed,
    };

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("macOS Keychain is only available on macOS.");
        }
    }

    private static class SecuritySymbols
    {
        private const string CoreFoundation =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private static readonly IntPtr SecurityHandle = NativeLibrary.Load(Security);
        private static readonly IntPtr CoreFoundationHandle = NativeLibrary.Load(CoreFoundation);

        internal static readonly IntPtr Class = Read(SecurityHandle, "kSecClass");
        internal static readonly IntPtr ClassGenericPassword = Read(SecurityHandle, "kSecClassGenericPassword");
        internal static readonly IntPtr AttributeService = Read(SecurityHandle, "kSecAttrService");
        internal static readonly IntPtr AttributeAccount = Read(SecurityHandle, "kSecAttrAccount");
        internal static readonly IntPtr ValueData = Read(SecurityHandle, "kSecValueData");
        internal static readonly IntPtr ReturnData = Read(SecurityHandle, "kSecReturnData");
        internal static readonly IntPtr MatchLimit = Read(SecurityHandle, "kSecMatchLimit");
        internal static readonly IntPtr MatchLimitOne = Read(SecurityHandle, "kSecMatchLimitOne");
        internal static readonly IntPtr BooleanTrue = Read(CoreFoundationHandle, "kCFBooleanTrue");

        private static IntPtr Read(IntPtr library, string symbol) =>
            Marshal.ReadIntPtr(NativeLibrary.GetExport(library, symbol));
    }
}
