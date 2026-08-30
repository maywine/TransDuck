// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Infrastructure.Persistence;

namespace TransDuck.Infrastructure.Tests.Persistence;

public sealed class JsonConfigurationStoreTests
{
    [Fact]
    public async Task ReadAsync_ReturnsNotFoundBeforeAnyWrite()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("configuration.json");
        using var store = new JsonConfigurationStore(filePath);

        var result = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.NotFound, result.Status);
        Assert.Null(result.Value);
        Assert.False(File.Exists(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_RoundTripsConfigurationAndLeavesNoTemporaryFiles()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("configuration.json");
        using var store = new JsonConfigurationStore(filePath);
        var configuration = PersistenceTestData.Configuration(maxEntries: 250, maxAgeDays: 45);

        var write = await store.WriteAsync(configuration, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        Assert.Equal(configuration, read.Value);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ReadAsync_MapsFutureVersionAndMalformedJsonToStableStatuses()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("configuration.json");
        using var store = new JsonConfigurationStore(filePath);
        var future = PersistenceTestData.Configuration() with { Version = ConfigurationMigration.CurrentVersion + 1 };

        await File.WriteAllBytesAsync(filePath, new UTF8Encoding(false).GetBytes(ContractJson.Serialize(future)));
        var futureRead = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(filePath, "{ malformed"u8.ToArray());
        var malformedRead = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.UnsupportedVersion, futureRead.Status);
        Assert.Equal(PersistenceStatus.InvalidData, malformedRead.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Operations_DistinguishPreCancellationFromDisposedStateAndDisposeRace()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("configuration.json");
        var configuration = PersistenceTestData.Configuration();
        using var cancellableStore = new JsonConfigurationStore(filePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await cancellableStore.WriteAsync(configuration, cancellation.Token);

        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);

        var raceStore = new JsonConfigurationStore(filePath);
        var operation = raceStore.WriteAsync(configuration, CancellationToken.None);
        raceStore.Dispose();
        var raced = await operation;
        var afterDispose = await raceStore.ReadAsync(CancellationToken.None);

        Assert.NotEqual(PersistenceStatus.Cancelled, raced.Status);
        Assert.Equal(PersistenceStatus.IoFailure, afterDispose.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ConcurrentWrites_LeaveOneValidConfigurationWithoutTemporaryFiles()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("configuration.json");
        using var store = new JsonConfigurationStore(filePath);
        var configurations = Enumerable.Range(1, 12)
            .Select(index => PersistenceTestData.Configuration(maxEntries: index, maxAgeDays: index))
            .ToArray();

        var writes = await Task.WhenAll(configurations.Select(configuration =>
            store.WriteAsync(configuration, CancellationToken.None)));
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.All(writes, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
        Assert.True(read.Succeeded);
        Assert.Contains(read.Value!, configurations);
        temporary.AssertNoTemporaryFiles();
    }
}
