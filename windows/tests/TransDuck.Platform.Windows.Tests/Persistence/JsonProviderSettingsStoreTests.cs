// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Core.Translation;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.Platform.Windows.Tests.Persistence;

public sealed class JsonProviderSettingsStoreTests
{
    [Fact]
    public async Task ReadAsync_ReturnsNotFoundAndConstructorDoesNotWrite()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("provider-settings.json");
        using var store = new JsonProviderSettingsStore(filePath);

        var read = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.NotFound, read.Status);
        Assert.Null(read.Value);
        Assert.False(File.Exists(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_RoundTripsAndCanonicalizesUnknownAdditiveFieldsWithoutSecrets()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("provider-settings.json");
        await File.WriteAllBytesAsync(filePath, new UTF8Encoding(false).GetBytes("""
            {"version":1,"profiles":[{"provider":{"providerId":"openai-compatible"},"endpoint":"https://provider.example.test/translate","model":"test-model","sourceLanguage":null,"targetLanguage":"zh-Hans","timeoutSeconds":30,"credential":"APIKEY_CANARY","futureOptionalHint":"ignored"}]}
            """));
        using var store = new JsonProviderSettingsStore(filePath);

        var initial = await store.ReadAsync(CancellationToken.None);
        var write = await store.WriteAsync(initial.Value!, CancellationToken.None);
        var roundTripped = await store.ReadAsync(CancellationToken.None);
        var raw = await File.ReadAllTextAsync(filePath);

        Assert.True(initial.Succeeded);
        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        AssertSettingsEqual(initial.Value!, roundTripped.Value!);
        Assert.DoesNotContain("futureOptionalHint", raw, StringComparison.Ordinal);
        AssertNoSecretSettingsContent(raw);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ReadAsync_MapsMalformedAndFutureVersionsToStableStatuses()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("provider-settings.json");
        using var store = new JsonProviderSettingsStore(filePath);
        await File.WriteAllBytesAsync(filePath, new UTF8Encoding(false).GetBytes("{ malformed"));
        var malformed = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(filePath, new UTF8Encoding(false).GetBytes("""
            {"version":2,"profiles":[]}
            """));
        var future = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.InvalidData, malformed.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, future.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_RejectsDuplicateCanonicalProviderAndInstance()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = new JsonProviderSettingsStore(temporary.FilePath("provider-settings.json"));
        var first = Profile("openai-compatible", "profile-a");
        var duplicate = first with { Endpoint = new Uri("https://other.example.test/translate") };
        var document = new ProviderSettingsDocument(ProviderSettingsMigration.CurrentVersion, [first, duplicate]);

        var result = await store.WriteAsync(document, CancellationToken.None);

        Assert.Equal(PersistenceStatus.InvalidData, result.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ConcurrentWrites_LeaveOneValidDocumentWithoutTemporaryFiles()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = new JsonProviderSettingsStore(temporary.FilePath("provider-settings.json"));
        var documents = Enumerable.Range(0, 12)
            .Select(index => new ProviderSettingsDocument(
                ProviderSettingsMigration.CurrentVersion,
                [Profile($"provider-{index:D2}")]))
            .ToArray();

        var writes = await Task.WhenAll(documents.Select(document =>
            store.WriteAsync(document, CancellationToken.None)));
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.All(writes, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
        Assert.True(read.Succeeded);
        Assert.Contains(documents, document => SettingsEqual(read.Value!, document));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Operations_DistinguishPreCancellationFromDisposedStateAndDisposeRace()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("provider-settings.json");
        var document = new ProviderSettingsDocument(ProviderSettingsMigration.CurrentVersion, [Profile("openai-compatible")]);
        using var cancellableStore = new JsonProviderSettingsStore(filePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await cancellableStore.WriteAsync(document, cancellation.Token);

        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);

        var raceStore = new JsonProviderSettingsStore(filePath);
        var operation = raceStore.WriteAsync(document, CancellationToken.None);
        raceStore.Dispose();
        var raced = await operation;
        var afterDispose = await raceStore.ReadAsync(CancellationToken.None);

        Assert.NotEqual(PersistenceStatus.Cancelled, raced.Status);
        Assert.Equal(PersistenceStatus.IoFailure, afterDispose.Status);
        temporary.AssertNoTemporaryFiles();
    }

    private static ProviderProfileSettings Profile(string providerId, string? instanceId = null) => new(
        new ProviderDescriptor(providerId, instanceId),
        new Uri("https://provider.example.test/translate"),
        "test-model",
        "en-US",
        "zh-Hans",
        30);

    private static void AssertNoSecretSettingsContent(string raw)
    {
        Assert.DoesNotContain("APIKEY_CANARY", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("credential", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", raw, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSettingsEqual(ProviderSettingsDocument expected, ProviderSettingsDocument actual)
    {
        Assert.True(SettingsEqual(expected, actual));
    }

    private static bool SettingsEqual(ProviderSettingsDocument first, ProviderSettingsDocument second) =>
        first.Version == second.Version &&
        first.Profiles.Count == second.Profiles.Count &&
        first.Profiles.Zip(second.Profiles).All(pair =>
            pair.First.Provider == pair.Second.Provider &&
            pair.First.Endpoint == pair.Second.Endpoint &&
            pair.First.Model == pair.Second.Model &&
            pair.First.SourceLanguage == pair.Second.SourceLanguage &&
            pair.First.TargetLanguage == pair.Second.TargetLanguage &&
            pair.First.TimeoutSeconds == pair.Second.TimeoutSeconds);
}
