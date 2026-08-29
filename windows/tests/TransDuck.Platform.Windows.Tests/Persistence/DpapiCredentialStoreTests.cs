// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.Platform.Windows.Tests.Persistence;

public sealed class DpapiCredentialStoreTests
{
    [Fact]
    public async Task CurrentUserDpapi_RoundTripsWithoutPersistingSecretOrProviderPlaintext()
    {
        const string canary = "APIKEY_CANARY_DPAPI_ROUNDTRIP";
        var key = new CredentialKey("provider-plain", "instance-plain");
        using var temporary = new PersistenceTestDirectory();
        var credentialsDirectory = temporary.DirectoryPath("credentials");
        using var store = new DpapiCredentialStore(credentialsDirectory);
        using var secret = new CredentialSecret(canary);

        var write = await store.SetAsync(key, secret, CancellationToken.None);
        var read = await store.GetAsync(key, CancellationToken.None);

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        using (var revealed = read.Value!)
        {
            Assert.Equal(canary, revealed.Reveal());
        }

        var credentialFile = Assert.Single(Directory.EnumerateFiles(credentialsDirectory, "*.credential"));
        var raw = await File.ReadAllBytesAsync(credentialFile);
        Assert.False(Path.GetFileName(credentialFile).Contains(key.ProviderId, StringComparison.Ordinal));
        Assert.False(Path.GetFileName(credentialFile).Contains(key.InstanceId!, StringComparison.Ordinal));
        Assert.False(ContainsUtf8(raw, canary));
        Assert.False(ContainsUtf8(raw, key.CanonicalValue));
        Assert.False(secret.ToString().Contains(canary, StringComparison.Ordinal));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task TamperRemoveAndNotFound_ReturnStableStatuses()
    {
        var key = new CredentialKey("provider-tamper", "instance-a");
        using var temporary = new PersistenceTestDirectory();
        var credentialsDirectory = temporary.DirectoryPath("credentials");
        using var store = new DpapiCredentialStore(credentialsDirectory);
        using var secret = new CredentialSecret("APIKEY_CANARY_DPAPI_TAMPER");

        Assert.Equal(PersistenceStatus.Succeeded, (await store.SetAsync(key, secret, CancellationToken.None)).Status);
        var credentialFile = Assert.Single(Directory.EnumerateFiles(credentialsDirectory, "*.credential"));
        await File.WriteAllBytesAsync(credentialFile, [0x00, 0x01, 0x02]);
        var tampered = await store.GetAsync(key, CancellationToken.None);
        var removed = await store.RemoveAsync(key, CancellationToken.None);
        var missing = await store.RemoveAsync(key, CancellationToken.None);

        Assert.Equal(PersistenceStatus.CorruptData, tampered.Status);
        Assert.Equal(PersistenceStatus.Succeeded, removed.Status);
        Assert.Equal(PersistenceStatus.NotFound, missing.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ConcurrentCredentials_RoundTripAndLeaveNoTemporaryFiles()
    {
        using var temporary = new PersistenceTestDirectory();
        var credentialsDirectory = temporary.DirectoryPath("credentials");
        using var store = new DpapiCredentialStore(credentialsDirectory);
        var keys = Enumerable.Range(0, 10)
            .Select(index => new CredentialKey($"provider-{index:D2}", "test"))
            .ToArray();
        var secrets = Enumerable.Range(0, 10)
            .Select(index => new CredentialSecret($"APIKEY_CANARY_DPAPI_{index:D2}"))
            .ToArray();

        try
        {
            var writes = await Task.WhenAll(keys.Select((key, index) =>
                store.SetAsync(key, secrets[index], CancellationToken.None)));
            var reads = await Task.WhenAll(keys.Select(key => store.GetAsync(key, CancellationToken.None)));

            Assert.All(writes, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
            for (var index = 0; index < reads.Length; index++)
            {
                Assert.True(reads[index].Succeeded);
                using var revealed = reads[index].Value!;
                Assert.Equal($"APIKEY_CANARY_DPAPI_{index:D2}", revealed.Reveal());
            }
        }
        finally
        {
            foreach (var secret in secrets)
            {
                secret.Dispose();
            }
        }

        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Operations_DistinguishPreCancellationFromDisposedStateAndDisposeRace()
    {
        var key = new CredentialKey("provider-dispose", "instance-a");
        using var temporary = new PersistenceTestDirectory();
        var credentialsDirectory = temporary.DirectoryPath("credentials");
        using var cancellableStore = new DpapiCredentialStore(credentialsDirectory);
        using var cancellation = new CancellationTokenSource();
        using var secret = new CredentialSecret("APIKEY_CANARY_DPAPI_DISPOSE");
        cancellation.Cancel();

        var cancelled = await cancellableStore.SetAsync(key, secret, cancellation.Token);

        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);

        var raceStore = new DpapiCredentialStore(credentialsDirectory);
        var operation = raceStore.SetAsync(key, secret, CancellationToken.None);
        raceStore.Dispose();
        var raced = await operation;
        var afterDispose = await raceStore.GetAsync(key, CancellationToken.None);

        Assert.NotEqual(PersistenceStatus.Cancelled, raced.Status);
        Assert.Equal(PersistenceStatus.IoFailure, afterDispose.Status);
        temporary.AssertNoTemporaryFiles();
    }

    private static bool ContainsUtf8(byte[] bytes, string value) =>
        bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(value)) >= 0;
}
