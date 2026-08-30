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
            new LocalDictionarySettings(true, temporary.FilePath("dictionary.csv")),
            true);

        var write = await store.WriteAsync(settings, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);
        var json = await File.ReadAllTextAsync(path);

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        Assert.Equal(settings.Version, read.Value!.Version);
        Assert.Equal(settings.EnabledTranslationProviders, read.Value.EnabledTranslationProviders);
        Assert.Equal(settings.LocalDictionary, read.Value.LocalDictionary);
        Assert.Equal(settings.MacSystemDictionaryEnabled, read.Value.MacSystemDictionaryEnabled);
        Assert.Contains("\"localDictionary\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ecdict\":", json, StringComparison.Ordinal);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ReadAsync_MigratesLegacyLocalDictionaryPropertyWithoutRewritingTheFile()
    {
        using var temporary = new PersistenceTestDirectory();
        var path = temporary.FilePath("query-sources.json");
        const string legacyJson = """
            {"version":1,"enabledTranslationProviders":[],"ecdict":{"enabled":true,"dataFilePath":"dictionary.csv"},"macSystemDictionaryEnabled":false}
            """;
        await File.WriteAllTextAsync(path, legacyJson, new UTF8Encoding(false));
        using var store = new JsonQuerySourceSettingsStore(path);

        var read = await store.ReadAsync(CancellationToken.None);
        var unchanged = await File.ReadAllTextAsync(path);

        Assert.True(read.Succeeded);
        Assert.True(read.Value!.LocalDictionary.Enabled);
        Assert.Equal("dictionary.csv", read.Value.LocalDictionary.DataFilePath);
        Assert.Equal(legacyJson, unchanged);

        var write = await store.WriteAsync(read.Value, CancellationToken.None);
        var migrated = await File.ReadAllTextAsync(path);

        Assert.True(write.Succeeded);
        Assert.Contains("\"localDictionary\":", migrated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"ecdict\":", migrated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_RejectsMissingAmbiguousOrMisCasedLocalDictionaryProperties()
    {
        using var temporary = new PersistenceTestDirectory();
        var path = temporary.FilePath("query-sources.json");
        using var store = new JsonQuerySourceSettingsStore(path);
        string[] invalidDocuments =
        [
            """
            {"version":1,"enabledTranslationProviders":[{"providerId":"deepl"}],"macSystemDictionaryEnabled":false}
            """,
            """
            {"version":1,"enabledTranslationProviders":[{"providerId":"deepl"}],"localDictionary":{"enabled":false},"ecdict":{"enabled":false},"macSystemDictionaryEnabled":false}
            """,
            """
            {"version":1,"enabledTranslationProviders":[{"providerId":"deepl"}],"LocalDictionary":{"enabled":false},"macSystemDictionaryEnabled":false}
            """,
            """
            {"version":1,"enabledTranslationProviders":[{"providerId":"deepl"}],"localDictionary":{"enabled":false},"localDictionary":{"enabled":false},"macSystemDictionaryEnabled":false}
            """,
        ];

        foreach (var document in invalidDocuments)
        {
            await File.WriteAllTextAsync(path, document, new UTF8Encoding(false));
            var read = await store.ReadAsync(CancellationToken.None);
            Assert.Equal(PersistenceStatus.InvalidData, read.Status);
        }
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
            {"version":2,"enabledTranslationProviders":[{"providerId":"deepl"}],"localDictionary":{"enabled":false},"macSystemDictionaryEnabled":false}
            """));
        var future = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(path, new UTF8Encoding(false).GetBytes("""
            {"version":2,"enabledTranslationProviders":[{"providerId":"deepl"}],"ecdict":{"enabled":false},"macSystemDictionaryEnabled":false}
            """));
        var legacyFuture = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.NotFound, missing.Status);
        Assert.Equal(PersistenceStatus.InvalidData, malformed.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, future.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, legacyFuture.Status);
    }
}
