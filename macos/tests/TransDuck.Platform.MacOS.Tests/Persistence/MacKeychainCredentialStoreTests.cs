using System.Security.Cryptography;
using TransDuck.Core.Persistence;
using TransDuck.Platform.MacOS.Persistence;

namespace TransDuck.Platform.MacOS.Tests.Persistence;

public sealed class MacKeychainCredentialStoreTests
{
    [Fact]
    public async Task SetGetAndRemove_UseServiceAndCanonicalProviderKeyWithoutFormattingSecret()
    {
        const string canary = "APIKEY_CANARY_MAC_KEYCHAIN";
        var backend = new FakeKeychainBackend();
        using var store = new MacKeychainCredentialStore(backend);
        var key = new CredentialKey("openai-compatible", "work");
        using var secret = new CredentialSecret(canary);

        var set = await store.SetAsync(key, secret, CancellationToken.None);
        var get = await store.GetAsync(key, CancellationToken.None);
        var remove = await store.RemoveAsync(key, CancellationToken.None);

        Assert.Equal(PersistenceStatus.Succeeded, set.Status);
        Assert.True(get.Succeeded);
        using (get.Value!)
        {
            Assert.Equal(canary, get.Value!.Reveal());
            Assert.DoesNotContain(canary, get.Value.ToString(), StringComparison.Ordinal);
        }

        Assert.Equal(PersistenceStatus.Succeeded, remove.Status);
        Assert.All(backend.Calls, call =>
        {
            Assert.Equal(MacKeychainCredentialStore.ServiceName, call.Service);
            Assert.Equal("openai-compatible:work", call.Account);
        });
        Assert.DoesNotContain(backend.Calls, call =>
            call.Service.Contains(canary, StringComparison.Ordinal) ||
            call.Account.Contains(canary, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(MacKeychainBackendStatus.NotFound, PersistenceStatus.NotFound)]
    [InlineData(MacKeychainBackendStatus.Denied, PersistenceStatus.IoFailure)]
    [InlineData(MacKeychainBackendStatus.Failed, PersistenceStatus.IoFailure)]
    public async Task Get_MapsStableBackendStatuses(
        MacKeychainBackendStatus backendStatus,
        PersistenceStatus expected)
    {
        var backend = new FakeKeychainBackend { ForcedReadStatus = backendStatus };
        using var store = new MacKeychainCredentialStore(backend);

        var result = await store.GetAsync(new CredentialKey("deepl"), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task CancellationAndInvalidKey_DoNotCallBackend()
    {
        var backend = new FakeKeychainBackend();
        using var store = new MacKeychainCredentialStore(backend);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await store.GetAsync(new CredentialKey("deepl"), cancellation.Token);
        var invalid = await store.RemoveAsync(new CredentialKey("invalid provider"), CancellationToken.None);

        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);
        Assert.Equal(PersistenceStatus.InvalidData, invalid.Status);
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public async Task CorruptEmptyBackendValue_IsNotExposedAsASecret()
    {
        var backend = new FakeKeychainBackend
        {
            ForcedReadStatus = MacKeychainBackendStatus.Succeeded,
            ForcedReadValue = [],
        };
        using var store = new MacKeychainCredentialStore(backend);

        var result = await store.GetAsync(new CredentialKey("deepl"), CancellationToken.None);

        Assert.Equal(PersistenceStatus.CorruptData, result.Status);
        Assert.Null(result.Value);
    }
}

internal sealed record KeychainCall(string Operation, string Service, string Account);

internal sealed class FakeKeychainBackend : IMacKeychainBackend
{
    private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

    public List<KeychainCall> Calls { get; } = [];

    public MacKeychainBackendStatus? ForcedReadStatus { get; init; }

    public byte[]? ForcedReadValue { get; init; }

    public MacKeychainReadResult Get(string service, string account)
    {
        Calls.Add(new KeychainCall("get", service, account));
        if (ForcedReadStatus is { } status)
        {
            return new MacKeychainReadResult(status, ForcedReadValue?.ToArray());
        }

        return _values.TryGetValue(Key(service, account), out var value)
            ? new MacKeychainReadResult(MacKeychainBackendStatus.Succeeded, value.ToArray())
            : new MacKeychainReadResult(MacKeychainBackendStatus.NotFound);
    }

    public MacKeychainBackendStatus Set(string service, string account, ReadOnlySpan<byte> value)
    {
        Calls.Add(new KeychainCall("set", service, account));
        _values[Key(service, account)] = value.ToArray();
        return MacKeychainBackendStatus.Succeeded;
    }

    public MacKeychainBackendStatus Remove(string service, string account)
    {
        Calls.Add(new KeychainCall("remove", service, account));
        return _values.Remove(Key(service, account))
            ? MacKeychainBackendStatus.Succeeded
            : MacKeychainBackendStatus.NotFound;
    }

    public void Dispose()
    {
        foreach (var value in _values.Values)
        {
            CryptographicOperations.ZeroMemory(value);
        }

        _values.Clear();
    }

    private static string Key(string service, string account) => service + "\n" + account;
}
