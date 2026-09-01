// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using Microsoft.Data.Sqlite;
using TransDuck.Core.Lookup;
using TransDuck.Infrastructure.Lookup;
using TransDuck.Infrastructure.Tests.Persistence;

namespace TransDuck.Infrastructure.Tests.Lookup;

public sealed class LocalDictionaryProviderTests
{
    [Fact]
    public void Registration_UsesGenericLocalDictionaryIdentity()
    {
        var provider = new LocalDictionaryProvider(Path.GetTempPath());

        Assert.Equal(LocalDictionaryIds.File, provider.Registration.ProviderId);
        Assert.Equal("Local dictionary", provider.Registration.DisplayName);
        Assert.True(provider.Registration.RequiresDataFile);
    }

    [Fact]
    public async Task LookupAsync_IndexesQuotedUtf8CsvAndReusesCache()
    {
        using var temporary = new PersistenceTestDirectory();
        var csvPath = temporary.FilePath("dictionary.csv");
        await File.WriteAllTextAsync(csvPath, """
            word,phonetic,definition,translation,pos,collins,oxford,tag,bnc,frq,exchange,detail,audio
            duck,dʌk,"a water bird,
            especially one with a broad bill","n. 鸭子
            v. 低头躲避",n:80/v:20,3,1,cet4,1,2,,,,
            "long-time",,"lasting for a long time",长期的,adj,0,0,,,,,,
            """, new UTF8Encoding(false));
        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));

        var first = await provider.LookupAsync("DUCK", csvPath, CancellationToken.None);
        var normalized = await provider.LookupAsync("long time", csvPath, CancellationToken.None);
        var cacheFilesAfterFirstLookups = Directory.GetFiles(temporary.DirectoryPath("cache"), "*.sqlite3");
        var repeated = await provider.LookupAsync("duck", csvPath, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.Equal("duck", first.Entry!.Term);
        Assert.Contains("鸭子", first.Entry.Translation, StringComparison.Ordinal);
        Assert.Contains("water bird,", first.Entry.Definition, StringComparison.Ordinal);
        Assert.True(normalized.Succeeded);
        Assert.Equal("long-time", normalized.Entry!.Term);
        Assert.Single(cacheFilesAfterFirstLookups);
        Assert.Equal(first.Entry, repeated.Entry);
    }

    [Fact]
    public async Task LookupAsync_DecodesOfficialEcdictCsvEscapes()
    {
        using var temporary = new PersistenceTestDirectory();
        var csvPath = temporary.FilePath("dictionary.csv");
        await File.WriteAllTextAsync(csvPath, """
            word,phonetic,definition,translation,pos,collins,oxford,tag,bnc,frq,exchange,detail,audio
            escaped,ph\n,"definition\nsecond\rthird\\slash\q\",translation\nnext,pos\q,0,0,,,,,,
            """, new UTF8Encoding(false));
        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));

        var result = await provider.LookupAsync("escaped", csvPath, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("ph\n", result.Entry!.Phonetic);
        Assert.Equal("definition\nsecond\rthird\\slash\\q\\", result.Entry.Definition);
        Assert.Equal("translation\nnext", result.Entry.Translation);
        Assert.Equal("pos\\q", result.Entry.PartOfSpeech);
    }

    [Fact]
    public async Task LookupAsync_RebuildsCacheWhenContentChangesWithoutSizeOrTimestampChange()
    {
        using var temporary = new PersistenceTestDirectory();
        var csvPath = temporary.FilePath("dictionary.csv");
        const string header = "word,phonetic,definition,translation,pos,collins,oxford,tag,bnc,frq,exchange,detail,audio\n";
        const string initialContent = header + "duck,,a water bird,first,n,0,0,,,,,,,\n";
        const string updatedContent = header + "duck,,a water bird,other,n,0,0,,,,,,,\n";
        Assert.Equal(Encoding.UTF8.GetByteCount(initialContent), Encoding.UTF8.GetByteCount(updatedContent));
        await File.WriteAllTextAsync(csvPath, initialContent, new UTF8Encoding(false));
        var sourceLength = new FileInfo(csvPath).Length;
        var sourceWriteTime = File.GetLastWriteTimeUtc(csvPath);
        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));

        var initial = await provider.LookupAsync("duck", csvPath, CancellationToken.None);
        await File.WriteAllTextAsync(csvPath, updatedContent, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(csvPath, sourceWriteTime);

        Assert.Equal(sourceLength, new FileInfo(csvPath).Length);
        Assert.Equal(sourceWriteTime, File.GetLastWriteTimeUtc(csvPath));

        var updated = await provider.LookupAsync("duck", csvPath, CancellationToken.None);

        Assert.True(initial.Succeeded);
        Assert.Equal("first", initial.Entry!.Translation);
        Assert.True(updated.Succeeded);
        Assert.Equal("other", updated.Entry!.Translation);
    }

    [Fact]
    public async Task LookupAsync_QueriesOfficialEcdictSqliteShapeWithoutModifyingIt()
    {
        using var temporary = new PersistenceTestDirectory();
        var databasePath = temporary.FilePath("stardict.db");
        await CreateDatabaseAsync(databasePath);
        var writeTime = File.GetLastWriteTimeUtc(databasePath);
        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));

        var found = await provider.LookupAsync("gave", databasePath, CancellationToken.None);
        var missing = await provider.LookupAsync("not-in-fixture", databasePath, CancellationToken.None);

        Assert.True(found.Succeeded);
        Assert.Equal("gave", found.Entry!.Term);
        Assert.Equal("give的过去式", found.Entry.Translation);
        Assert.Equal(DictionaryLookupStatus.NotFound, missing.Status);
        Assert.Equal(writeTime, File.GetLastWriteTimeUtc(databasePath));
        Assert.False(Directory.Exists(temporary.DirectoryPath("cache")));
    }

    [Fact]
    public async Task LookupAsync_PrefersExactWordOverNormalizedAlternative()
    {
        using var temporary = new PersistenceTestDirectory();
        var databasePath = temporary.FilePath("stardict.db");
        await CreateDatabaseAsync(databasePath);
        await ExecuteDatabaseAsync(databasePath, """
            INSERT INTO stardict(word, sw, phonetic, definition, translation, pos)
            VALUES ('long-time', 'longtime', NULL, NULL, 'normalized', NULL),
                   ('longtime', 'longtime', NULL, NULL, 'exact', NULL);
            """);
        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));

        var result = await provider.LookupAsync("LONGTIME", databasePath, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("longtime", result.Entry!.Term);
        Assert.Equal("exact", result.Entry.Translation);
    }

    [Fact]
    public async Task LookupAsync_UsesNormalizedFallbackWhenExactWordIsAbsent()
    {
        using var temporary = new PersistenceTestDirectory();
        var databasePath = temporary.FilePath("stardict.db");
        await CreateDatabaseAsync(databasePath);
        await ExecuteDatabaseAsync(databasePath, """
            INSERT INTO stardict(word, sw, phonetic, definition, translation, pos)
            VALUES ('long-time', 'longtime', NULL, NULL, 'normalized', NULL);
            """);
        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));

        var result = await provider.LookupAsync("long time", databasePath, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("long-time", result.Entry!.Term);
        Assert.Equal("normalized", result.Entry.Translation);
    }

    [Fact]
    public async Task LookupSql_UsesIndexedPlansWithoutScanningTheDictionary()
    {
        using var temporary = new PersistenceTestDirectory();
        var databasePath = temporary.FilePath("stardict.db");
        await CreateDatabaseAsync(databasePath);

        var exactPlan = await ExplainQueryPlanAsync(
            databasePath,
            LocalDictionaryProvider.ExactLookupSql,
            "gave");
        var normalizedPlan = await ExplainQueryPlanAsync(
            databasePath,
            LocalDictionaryProvider.NormalizedLookupSql,
            "gave");

        Assert.Contains(exactPlan, detail =>
            detail.Contains("SEARCH", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("word=?", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(normalizedPlan, detail =>
            detail.Contains("SEARCH", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("stardict_sw", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(exactPlan, detail =>
            detail.Contains("SCAN stardict", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(normalizedPlan, detail =>
            detail.Contains("SCAN stardict", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LookupAsync_ReturnsNotFoundWhenMatchedEntryHasNoDefinitions()
    {
        using var temporary = new PersistenceTestDirectory();
        var databasePath = temporary.FilePath("stardict.db");
        await CreateDatabaseAsync(databasePath);
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO stardict(word, sw, phonetic, definition, translation, pos)
                VALUES ('empty', 'exact-empty', NULL, NULL, NULL, NULL),
                       ('empty!', 'empty', NULL, NULL, 'fallback', NULL);
                """;
            await command.ExecuteNonQueryAsync();
        }

        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));
        var result = await provider.LookupAsync("empty", databasePath, CancellationToken.None);

        Assert.Equal(DictionaryLookupStatus.NotFound, result.Status);
        Assert.Null(result.Entry);
    }

    [Fact]
    public async Task LookupAsync_ReportsMissingAndInvalidFilesWithoutLeakingExceptions()
    {
        using var temporary = new PersistenceTestDirectory();
        var provider = new LocalDictionaryProvider(temporary.DirectoryPath("cache"));
        var invalidPath = temporary.FilePath("invalid.csv");
        await File.WriteAllTextAsync(invalidPath, "wrong,headers\nvalue,other", new UTF8Encoding(false));

        var missing = await provider.LookupAsync("duck", temporary.FilePath("missing.db"), CancellationToken.None);
        var invalid = await provider.LookupAsync("duck", invalidPath, CancellationToken.None);
        var empty = await provider.LookupAsync(" ", invalidPath, CancellationToken.None);

        Assert.Equal(DictionaryLookupStatus.Unavailable, missing.Status);
        Assert.Equal(DictionaryLookupStatus.InvalidData, invalid.Status);
        Assert.Equal(DictionaryLookupStatus.InvalidRequest, empty.Status);
    }

    private static async Task CreateDatabaseAsync(string path)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE stardict (
                id INTEGER PRIMARY KEY,
                word TEXT COLLATE NOCASE NOT NULL UNIQUE,
                sw TEXT COLLATE NOCASE NOT NULL,
                phonetic TEXT,
                definition TEXT,
                translation TEXT,
                pos TEXT
            );
            CREATE INDEX stardict_sw ON stardict(sw, word COLLATE NOCASE);
            INSERT INTO stardict(word, sw, phonetic, definition, translation, pos)
            VALUES ('gave', 'gave', 'ɡeɪv', 'past tense of give', 'give的过去式', 'v');
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteDatabaseAsync(string path, string commandText)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ExplainQueryPlanAsync(
        string path,
        string query,
        string value)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + query;
        command.Parameters.AddWithValue("$value", value);
        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            details.Add(reader.GetString(3));
        }

        return details;
    }
}
