// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.Platform.Windows.Tests.Persistence;

public sealed class JsonLinesHistoryStoreTests
{
    [Fact]
    public async Task ReadAsync_AppliesDeterministicAgeAndEntryRetentionInNewestOrder()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        using var temporary = new PersistenceTestDirectory();
        using var store = new JsonLinesHistoryStore(
            temporary.FilePath("history.jsonl"),
            new FixedTimeProvider(now));
        var unbounded = new HistoryRetention(0, 0);
        var old = PersistenceTestData.HistoryEntry("history-old", now.AddDays(-31));
        var middle = PersistenceTestData.HistoryEntry("history-middle", now.AddDays(-2));
        var newest = PersistenceTestData.HistoryEntry("history-newest", now.AddDays(-1));

        await store.AppendAsync(old, unbounded, CancellationToken.None);
        await store.AppendAsync(middle, unbounded, CancellationToken.None);
        await store.AppendAsync(newest, unbounded, CancellationToken.None);
        var read = await store.ReadAsync(new HistoryRetention(2, 30), CancellationToken.None);

        Assert.True(read.Succeeded);
        Assert.Equal(new[] { newest.EntryId, middle.EntryId }, read.Entries.Select(entry => entry.EntryId));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ReadAndAppend_PreserveValidLinesAndReportCorruption()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("history.jsonl");
        var valid = PersistenceTestData.HistoryEntry("history-valid", now);
        await File.WriteAllBytesAsync(
            filePath,
            new UTF8Encoding(false).GetBytes(ContractJson.Serialize(valid) + "\n{ malformed\n"));
        using var store = new JsonLinesHistoryStore(filePath, new FixedTimeProvider(now));
        var retention = new HistoryRetention(0, 0);

        var read = await store.ReadAsync(retention, CancellationToken.None);
        var append = await store.AppendAsync(
            PersistenceTestData.HistoryEntry("history-appended", now.AddMinutes(1)),
            retention,
            CancellationToken.None);
        var compacted = await store.ReadAsync(retention, CancellationToken.None);

        Assert.Equal(PersistenceStatus.Succeeded, read.Status);
        Assert.Equal(1, read.CorruptLineCount);
        Assert.Single(read.Entries);
        Assert.Equal(PersistenceStatus.Succeeded, append.Status);
        Assert.Equal(1, append.CorruptLineCount);
        Assert.Equal(0, compacted.CorruptLineCount);
        Assert.Equal(2, compacted.Entries.Count);
        Assert.DoesNotContain("malformed", await File.ReadAllTextAsync(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ClearAsync_ReturnsNotFoundThenAtomicallyClearsExistingHistory()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("history.jsonl");
        using var store = new JsonLinesHistoryStore(filePath, new FixedTimeProvider(now));
        var retention = new HistoryRetention(0, 0);

        var missing = await store.ClearAsync(CancellationToken.None);
        await store.AppendAsync(PersistenceTestData.HistoryEntry("history-clear", now), retention, CancellationToken.None);
        var cleared = await store.ClearAsync(CancellationToken.None);
        var read = await store.ReadAsync(retention, CancellationToken.None);

        Assert.Equal(PersistenceStatus.NotFound, missing.Status);
        Assert.Equal(PersistenceStatus.Succeeded, cleared.Status);
        Assert.Equal(PersistenceStatus.Succeeded, read.Status);
        Assert.Empty(read.Entries);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Operations_DistinguishCancellationFromDisposedStateAndDisposeRace()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("history.jsonl");
        var entry = PersistenceTestData.HistoryEntry("history-race", now);
        var retention = new HistoryRetention(0, 0);
        using var cancellableStore = new JsonLinesHistoryStore(filePath, new FixedTimeProvider(now));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await cancellableStore.AppendAsync(entry, retention, cancellation.Token);

        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);

        var raceStore = new JsonLinesHistoryStore(filePath, new FixedTimeProvider(now));
        var operation = raceStore.AppendAsync(entry, retention, CancellationToken.None);
        raceStore.Dispose();
        var raced = await operation;
        var afterDispose = await raceStore.ReadAsync(retention, CancellationToken.None);

        Assert.NotEqual(PersistenceStatus.Cancelled, raced.Status);
        Assert.Equal(PersistenceStatus.IoFailure, afterDispose.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ConcurrentAppends_RemainReadableAndLeaveNoTemporaryFiles()
    {
        var now = new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);
        using var temporary = new PersistenceTestDirectory();
        using var store = new JsonLinesHistoryStore(
            temporary.FilePath("history.jsonl"),
            new FixedTimeProvider(now));
        var retention = new HistoryRetention(0, 0);
        var entries = Enumerable.Range(0, 16)
            .Select(index => PersistenceTestData.HistoryEntry(
                $"history-concurrent-{index:D2}",
                now.AddMinutes(index)))
            .ToArray();

        var appends = await Task.WhenAll(entries.Select(entry =>
            store.AppendAsync(entry, retention, CancellationToken.None)));
        var read = await store.ReadAsync(retention, CancellationToken.None);

        Assert.All(appends, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
        Assert.Equal(16, read.Entries.Count);
        Assert.Equal(
            read.Entries.Select(entry => entry.EntryId).OrderByDescending(id => id, StringComparer.Ordinal),
            read.Entries.Select(entry => entry.EntryId));
        temporary.AssertNoTemporaryFiles();
    }
}
