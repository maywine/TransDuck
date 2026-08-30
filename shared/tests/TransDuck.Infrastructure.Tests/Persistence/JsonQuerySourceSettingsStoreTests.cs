// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Lookup;
using TransDuck.Core.Persistence;
using TransDuck.Infrastructure.Persistence;

namespace TransDuck.Infrastructure.Tests.Persistence;

public sealed class JsonQuerySourceSettingsStoreTests
{
    [Fact]
    public async Task WriteAsync_RoundTripsMultipleProvidersAndLocalDictionary()
    {
        using var temporary = new PersistenceTestDirectory();
        var path = temporary.FilePath("query-sources.json");
        using var store = new JsonQuerySourceSettingsStore(path);
        var settings = new QuerySourceSettings(
            1,
            [new ProviderDescriptor("deepl"), new ProviderDescriptor("ollama")],
            new EcdictDictionarySettings(true, temporary.FilePath("ecdict.csv")),
            true);

        var write = await store.WriteAsync(settings, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        Assert.Equal(settings.Version, read.Value!.Version);
        Assert.Equal(settings.EnabledTranslationProviders, read.Value.EnabledTranslationProviders);
        Assert.Equal(settings.Ecdict, read.Value.Ecdict);
        Assert.Equal(settings.MacSystemDictionaryEnabled, read.Value.MacSystemDictionaryEnabled);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ReadAsync_DistinguishesInvalidFutureAndMissingDocuments()
    {
        using var temporary = new PersistenceTestDirectory();
        var path = temporary.FilePath("query-sources.json");
        using var store = new JsonQuerySourceSettingsStore(path);
        var missing = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(path, new UTF8Encoding(false).GetBytes("{ malformed"));
        var malformed = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(path, new UTF8Encoding(false).GetBytes("""
            {"version":2,"enabledTranslationProviders":[{"providerId":"deepl"}],"ecdict":{"enabled":false},"macSystemDictionaryEnabled":false}
            """));
        var future = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.NotFound, missing.Status);
        Assert.Equal(PersistenceStatus.InvalidData, malformed.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, future.Status);
    }
}
