// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Hotkeys;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.Platform.Windows.Tests.Persistence;

public sealed class JsonHotkeySettingsStoreTests
{
    [Fact]
    public async Task ConstructorAndReadAsync_DoNotWriteToTheRealApplicationPath()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);

        var read = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.NotFound, read.Status);
        Assert.Null(read.Value);
        Assert.False(File.Exists(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_RoundTripsSafeSettingsAtomically()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var settings = Settings(alt: true, shift: true, virtualKey: 0x7B);

        var write = await store.WriteAsync(settings, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        Assert.Equal(settings, read.Value);
        Assert.Equal(
            new[] { "alt", "control", "shift", "version", "virtualKey", "windows" },
            document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        AssertNoSensitiveHotkeyContent(await File.ReadAllTextAsync(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ReadAsync_MapsMalformedFutureAndInvalidDocumentsToStableStatuses()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var future = Settings(version: HotkeySettingsMigration.CurrentVersion + 1);
        var invalid = Settings(control: false, alt: false, shift: false, windows: false);

        await File.WriteAllBytesAsync(filePath, "{ malformed"u8.ToArray());
        var malformedRead = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(filePath, new UTF8Encoding(false).GetBytes(
            JsonSerializer.Serialize(future, ContractJson.SerializerOptions)));
        var futureRead = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(filePath, new UTF8Encoding(false).GetBytes(
            JsonSerializer.Serialize(invalid, ContractJson.SerializerOptions)));
        var invalidRead = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.InvalidData, malformedRead.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, futureRead.Status);
        Assert.Equal(PersistenceStatus.InvalidData, invalidRead.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_RejectsInvalidAndFutureSettingsWithoutWriting()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var invalid = Settings(control: false, alt: false, shift: false, windows: false);
        var future = Settings(version: HotkeySettingsMigration.CurrentVersion + 1);

        var invalidWrite = await store.WriteAsync(invalid, CancellationToken.None);
        var futureWrite = await store.WriteAsync(future, CancellationToken.None);

        Assert.Equal(PersistenceStatus.InvalidData, invalidWrite.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, futureWrite.Status);
        Assert.False(File.Exists(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ConcurrentWrites_LeaveOneValidSettingsDocumentWithoutTemporaryFiles()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out _);
        var settings = Enumerable.Range(0, 12)
            .Select(index => Settings(
                control: index % 2 == 0,
                alt: index % 2 != 0,
                shift: index % 3 == 0,
                virtualKey: (uint)(0x41 + index)))
            .ToArray();

        var writes = await Task.WhenAll(settings.Select(candidate =>
            store.WriteAsync(candidate, CancellationToken.None)));
        var read = await store.ReadAsync(CancellationToken.None);

        Assert.All(writes, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
        Assert.True(read.Succeeded);
        Assert.Contains(read.Value!, settings);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Operations_DistinguishPreCancellationFromDisposedStateAndDisposeRace()
    {
        using var temporary = new PersistenceTestDirectory();
        var settings = Settings();
        using var cancellableStore = CreateStore(temporary, out _);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await cancellableStore.WriteAsync(settings, cancellation.Token);

        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);

        var raceStore = CreateStore(temporary, out _);
        var operation = raceStore.WriteAsync(settings, CancellationToken.None);
        raceStore.Dispose();
        var raced = await operation;
        var afterDispose = await raceStore.ReadAsync(CancellationToken.None);

        Assert.NotEqual(PersistenceStatus.Cancelled, raced.Status);
        Assert.Equal(PersistenceStatus.IoFailure, afterDispose.Status);
        temporary.AssertNoTemporaryFiles();
    }

    private static JsonHotkeySettingsStore CreateStore(
        PersistenceTestDirectory temporary,
        out string filePath)
    {
        filePath = temporary.FilePath("hotkey-settings.json");
        var temporaryRoot = Path.GetFullPath(temporary.RootDirectory).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var applicationFilePath = Path.GetFullPath(new WindowsDataPaths().HotkeySettingsFilePath);

        Assert.StartsWith(temporaryRoot, Path.GetFullPath(filePath));
        Assert.False(string.Equals(
            Path.GetFullPath(filePath),
            applicationFilePath,
            StringComparison.OrdinalIgnoreCase));
        return new JsonHotkeySettingsStore(filePath);
    }

    private static HotkeySettings Settings(
        int version = HotkeySettingsMigration.CurrentVersion,
        bool control = true,
        bool alt = false,
        bool shift = false,
        bool windows = false,
        uint virtualKey = 0x44) => new(version, control, alt, shift, windows, virtualKey);

    private static void AssertNoSensitiveHotkeyContent(string content)
    {
        Assert.DoesNotContain("APIKEY_CANARY", content, StringComparison.Ordinal);
        Assert.DoesNotContain("QUERY_CANARY", content, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIPBOARD_CANARY", content, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("message", content, StringComparison.OrdinalIgnoreCase);
    }
}
