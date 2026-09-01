// Copyright (c) 2026 maywine. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.VisualBasic.FileIO;
using TransDuck.Core.Lookup;

namespace TransDuck.Infrastructure.Lookup;

/// <summary>
/// Queries supported local SQLite dictionaries and indexes compatible UTF-8 CSV files into an app-owned cache.
/// </summary>
public sealed class LocalDictionaryProvider : IDictionaryProvider
{
    private const string SqliteSignature = "SQLite format 3\0";
    internal const string ExactLookupSql = """
        SELECT word, phonetic, definition, translation, pos
        FROM stardict
        WHERE word = $value COLLATE NOCASE
        LIMIT 1;
        """;
    internal const string NormalizedLookupSql = """
        SELECT word, phonetic, definition, translation, pos
        FROM stardict
        WHERE sw = $value COLLATE NOCASE
        ORDER BY word COLLATE NOCASE
        LIMIT 1;
        """;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CacheGates =
        new(StringComparer.Ordinal);
    private readonly string _cacheDirectory;

    public LocalDictionaryProvider(string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
    }

    public DictionaryProviderRegistration Registration { get; } = new(
        LocalDictionaryIds.File,
        "Local dictionary",
        RequiresDataFile: true);

    public async Task<DictionaryLookupResult> LookupAsync(
        string text,
        string? dataFilePath,
        CancellationToken cancellationToken)
    {
        var term = text?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length > 512 ||
            string.IsNullOrWhiteSpace(dataFilePath))
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.InvalidRequest);
        }

        string sourcePath;
        try
        {
            sourcePath = Path.GetFullPath(dataFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.InvalidRequest);
        }

        if (!File.Exists(sourcePath))
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Unavailable);
        }

        try
        {
            var format = await DetectFormatAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var databasePath = format == LocalDictionaryFileFormat.Sqlite
                ? sourcePath
                : await EnsureCsvCacheAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            return await QuerySqliteAsync(databasePath, term, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Cancelled);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or SqliteException or MalformedLineException or
                ArgumentException or DecoderFallbackException)
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.InvalidData);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.Unavailable);
        }
    }

    private async Task<string> EnsureCsvCacheAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = Path.Combine(_cacheDirectory, CacheFileName(sourcePath));
        var gate = CacheGates.GetOrAdd(cachePath, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = await ReadSourceMetadataAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            if (await IsCurrentCacheAsync(cachePath, source, cancellationToken).ConfigureAwait(false))
            {
                return cachePath;
            }

            await BuildCacheAsync(sourcePath, cachePath, source, cancellationToken).ConfigureAwait(false);
            return cachePath;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<bool> IsCurrentCacheAsync(
        string cachePath,
        SourceMetadata source,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            await using var connection = new SqliteConnection(ReadOnlyConnectionString(cachePath));
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT source_length, source_write_ticks, source_sha256
                FROM transduck_metadata
                LIMIT 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) &&
                reader.GetInt64(0) == source.Length &&
                reader.GetInt64(1) == source.LastWriteTicks &&
                string.Equals(reader.GetString(2), source.ContentSha256, StringComparison.Ordinal);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static async Task BuildCacheAsync(
        string sourcePath,
        string cachePath,
        SourceMetadata source,
        CancellationToken cancellationToken)
    {
        var temporaryPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await Task.Run(
                () => ImportCsv(sourcePath, temporaryPath, source, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var currentSource = await ReadSourceMetadataAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            if (currentSource != source)
            {
                throw new IOException("The local dictionary CSV file changed while it was being indexed.");
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ImportCsv(
        string sourcePath,
        string databasePath,
        SourceMetadata source,
        CancellationToken cancellationToken)
    {
        using var parser = new TextFieldParser(sourcePath, StrictUtf8, detectEncoding: true)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ?? throw new InvalidDataException("The local dictionary CSV is empty.");
        var columns = headers
            .Select((name, index) => new KeyValuePair<string, int>(name.Trim(), index))
            .ToDictionary(static item => item.Key, static item => item.Value, StringComparer.OrdinalIgnoreCase);
        var wordColumn = RequiredColumn(columns, "word");
        var phoneticColumn = RequiredColumn(columns, "phonetic");
        var definitionColumn = RequiredColumn(columns, "definition");
        var translationColumn = RequiredColumn(columns, "translation");
        var partOfSpeechColumn = RequiredColumn(columns, "pos");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString());
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                PRAGMA journal_mode=OFF;
                PRAGMA synchronous=OFF;
                PRAGMA temp_store=MEMORY;
                CREATE TABLE stardict (
                    word TEXT COLLATE NOCASE PRIMARY KEY NOT NULL,
                    sw TEXT COLLATE NOCASE NOT NULL,
                    phonetic TEXT,
                    definition TEXT,
                    translation TEXT,
                    pos TEXT
                );
                CREATE TABLE transduck_metadata (
                    source_length INTEGER NOT NULL,
                    source_write_ticks INTEGER NOT NULL,
                    source_sha256 TEXT NOT NULL
                );
                """;
            schema.ExecuteNonQuery();
        }

        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT OR REPLACE INTO stardict(word, sw, phonetic, definition, translation, pos)
            VALUES ($word, $sw, $phonetic, $definition, $translation, $pos);
            """;
        var wordParameter = insert.Parameters.Add("$word", SqliteType.Text);
        var stripWordParameter = insert.Parameters.Add("$sw", SqliteType.Text);
        var phoneticParameter = insert.Parameters.Add("$phonetic", SqliteType.Text);
        var definitionParameter = insert.Parameters.Add("$definition", SqliteType.Text);
        var translationParameter = insert.Parameters.Add("$translation", SqliteType.Text);
        var partOfSpeechParameter = insert.Parameters.Add("$pos", SqliteType.Text);
        while (!parser.EndOfData)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.ReadFields();
            if (fields is null)
            {
                continue;
            }
            var word = DecodeCsvValue(Field(fields, wordColumn))?.Trim();
            if (string.IsNullOrWhiteSpace(word))
            {
                continue;
            }

            wordParameter.Value = word;
            stripWordParameter.Value = StripWord(word);
            phoneticParameter.Value = DatabaseValue(DecodeCsvValue(Field(fields, phoneticColumn)));
            definitionParameter.Value = DatabaseValue(DecodeCsvValue(Field(fields, definitionColumn)));
            translationParameter.Value = DatabaseValue(DecodeCsvValue(Field(fields, translationColumn)));
            partOfSpeechParameter.Value = DatabaseValue(DecodeCsvValue(Field(fields, partOfSpeechColumn)));
            insert.ExecuteNonQuery();
        }

        using (var index = connection.CreateCommand())
        {
            index.Transaction = transaction;
            index.CommandText = "CREATE INDEX stardict_sw ON stardict(sw, word COLLATE NOCASE);";
            index.ExecuteNonQuery();
        }

        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = """
                INSERT INTO transduck_metadata(source_length, source_write_ticks, source_sha256)
                VALUES ($length, $ticks, $sha256);
                """;
            metadata.Parameters.AddWithValue("$length", source.Length);
            metadata.Parameters.AddWithValue("$ticks", source.LastWriteTicks);
            metadata.Parameters.AddWithValue("$sha256", source.ContentSha256);
            metadata.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static async Task<DictionaryLookupResult> QuerySqliteAsync(
        string databasePath,
        string term,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(ReadOnlyConnectionString(databasePath));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSupportedSchemaAsync(connection, cancellationToken).ConfigureAwait(false);
        var entry = await QueryEntryAsync(
            connection,
            ExactLookupSql,
            term,
            cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            entry = await QueryEntryAsync(
                connection,
                NormalizedLookupSql,
                StripWord(term),
                cancellationToken).ConfigureAwait(false);
        }

        if (entry is null ||
            (string.IsNullOrWhiteSpace(entry.Translation) && string.IsNullOrWhiteSpace(entry.Definition)))
        {
            return DictionaryLookupResult.FromStatus(DictionaryLookupStatus.NotFound);
        }

        return DictionaryLookupResult.Found(entry);
    }

    private static async Task<DictionaryLookupEntry?> QueryEntryAsync(
        SqliteConnection connection,
        string commandText,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$value", value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new DictionaryLookupEntry(
            reader.GetString(0),
            NullableString(reader, 1),
            NullableString(reader, 3),
            NullableString(reader, 2),
            NullableString(reader, 4));
    }

    private static async Task EnsureSupportedSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('stardict');";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            columns.Add(reader.GetString(1));
        }

        string[] required = ["word", "sw", "phonetic", "definition", "translation", "pos"];
        if (required.Any(column => !columns.Contains(column)))
        {
            throw new InvalidDataException("The SQLite file does not contain the supported local dictionary schema.");
        }
    }

    private static async Task<LocalDictionaryFileFormat> DetectFormatAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var signature = new byte[Encoding.ASCII.GetByteCount(SqliteSignature)];
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytesRead = await stream.ReadAsync(signature, cancellationToken).ConfigureAwait(false);
        if (bytesRead == signature.Length &&
            signature.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(SqliteSignature)))
        {
            return LocalDictionaryFileFormat.Sqlite;
        }

        return LocalDictionaryFileFormat.Csv;
    }

    private static string ReadOnlyConnectionString(string databasePath) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

    private static string CacheFileName(string sourcePath)
    {
        var normalized = Path.GetFullPath(sourcePath).Normalize(NormalizationForm.FormC);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash) + ".sqlite3";
    }

    private static string StripWord(string word) => string.Concat(
        word.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant));

    private static async Task<SourceMetadata> ReadSourceMetadataAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var before = new FileInfo(sourcePath);
        var length = before.Length;
        var writeTicks = before.LastWriteTimeUtc.Ticks;
        await using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var after = new FileInfo(sourcePath);
        if (after.Length != length || after.LastWriteTimeUtc.Ticks != writeTicks)
        {
            throw new IOException("The local dictionary CSV file changed while its checksum was being calculated.");
        }

        return new SourceMetadata(length, writeTicks, Convert.ToHexStringLower(hash));
    }

    private static string? DecodeCsvValue(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var decoded = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character != '\\')
            {
                decoded.Append(character);
                continue;
            }

            if (index + 1 >= value.Length)
            {
                decoded.Append('\\');
                continue;
            }

            var escaped = value[++index];
            decoded.Append(escaped switch
            {
                '\\' => '\\',
                'n' => '\n',
                'r' => '\r',
                _ => '\\',
            });
            if (escaped is not ('\\' or 'n' or 'r'))
            {
                decoded.Append(escaped);
            }
        }

        return decoded.ToString();
    }

    private static int RequiredColumn(IReadOnlyDictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out var index)
            ? index
            : throw new InvalidDataException(
                string.Format(CultureInfo.InvariantCulture, "Local dictionary CSV is missing the {0} column.", name));

    private static string? Field(IReadOnlyList<string> fields, int index) =>
        index < fields.Count ? fields[index] : null;

    private static object DatabaseValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private sealed record SourceMetadata(long Length, long LastWriteTicks, string ContentSha256);

    private enum LocalDictionaryFileFormat
    {
        Csv,
        Sqlite,
    }
}
