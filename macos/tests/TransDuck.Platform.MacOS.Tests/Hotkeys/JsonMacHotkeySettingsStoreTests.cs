using System.Text;
using System.Text.Json;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;
using TransDuck.Platform.MacOS.Hotkeys;

namespace TransDuck.Platform.MacOS.Tests.Hotkeys;

public sealed class JsonMacHotkeySettingsStoreTests
{
    [Fact]
    public async Task WriteAndRead_RoundTripWithoutCreatingSecretFields()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("hotkey-settings.v1.json");
        using var store = new JsonMacHotkeySettingsStore(path);
        var settings = new MacHotkeySettings(
            MacHotkeySettingsMigration.CurrentVersion,
            MacHotkeyModifiers.Control | MacHotkeyModifiers.Shift,
            MacVirtualKey.F12);

        var write = await store.WriteAsync(settings, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        Assert.Equal(settings, read.Value);
        Assert.Equal("control, shift", document.RootElement.GetProperty("modifiers").GetString());
        Assert.Equal("f12", document.RootElement.GetProperty("key").GetString());
        Assert.DoesNotContain("credential", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Read_MapsMalformedInvalidAndFutureDocumentsToStableStatuses()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("hotkey-settings.v1.json");
        using var store = new JsonMacHotkeySettingsStore(path);

        await File.WriteAllBytesAsync(path, "{ malformed"u8.ToArray());
        var malformed = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllTextAsync(path, """
            {"version":1,"modifiers":"none","key":"d"}
            """);
        var invalid = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllTextAsync(path, """
            {"version":1,"modifiers":"option","key":"d"}
            """);
        var textProducing = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            MacHotkeySettings.Default with { Version = MacHotkeySettingsMigration.CurrentVersion + 1 },
            ContractJson.SerializerOptions));
        var future = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.InvalidData, malformed.Status);
        Assert.Equal(PersistenceStatus.InvalidData, invalid.Status);
        Assert.Equal(PersistenceStatus.InvalidData, textProducing.Status);
        Assert.Equal(PersistenceStatus.InvalidData, future.Status);
    }

    [Fact]
    public async Task Write_RejectsFutureAndCancellationBeforeFilesystemMutation()
    {
        using var temporary = new TemporaryDirectory();
        var path = temporary.FilePath("hotkey-settings.v1.json");
        using var store = new JsonMacHotkeySettingsStore(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var future = await store.WriteAsync(
            MacHotkeySettings.Default with { Version = MacHotkeySettingsMigration.CurrentVersion + 1 },
            CancellationToken.None);
        var cancelled = await store.WriteAsync(MacHotkeySettings.Default, cancellation.Token);

        Assert.Equal(PersistenceStatus.UnsupportedVersion, future.Status);
        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);
        Assert.False(File.Exists(path));
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "TransDuck.MacOS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string FilePath(string fileName) => Path.Combine(Root, fileName);

    public void AssertNoTemporaryFiles() => Assert.Empty(Directory.EnumerateFiles(Root, "*.tmp"));

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
