// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using System.Text.Json;
using TransDuck.Core.Persistence;
using TransDuck.Platform.Windows.Persistence;
using TransDuck.Platform.Windows.Proxy;
using TransDuck.Platform.Windows.Tests.Persistence;

namespace TransDuck.Platform.Windows.Tests.Proxy;

public sealed class JsonWindowsProxySettingsStoreTests
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
    public async Task WriteAsync_RoundTripsCustomHttpSettingsAtomicallyWithoutCredentialFields()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var settings = Custom("http://proxy.example.test:8080");

        var write = await store.WriteAsync(settings, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
        var properties = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        Assert.Equal(settings, read.Value);
        Assert.Equal(new[] { "customHttpProxyUri", "mode", "version" }, properties);
        Assert.Equal("customHttp", document.RootElement.GetProperty("mode").GetString());
        Assert.DoesNotContain(properties, name =>
            name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("user", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("auth", StringComparison.OrdinalIgnoreCase));
        temporary.AssertNoTemporaryFiles();
    }

    [Theory]
    [InlineData(WindowsProxyMode.SystemDefault, "systemDefault")]
    [InlineData(WindowsProxyMode.Disabled, "disabled")]
    public async Task WriteAsync_RoundTripsNonCustomModesWithoutSerializingANullProxyUri(
        WindowsProxyMode mode,
        string expectedJsonMode)
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var settings = new WindowsProxySettings(
            WindowsProxySettingsMigration.CurrentVersion,
            mode,
            CustomHttpProxyUri: null);

        var write = await store.WriteAsync(settings, CancellationToken.None);
        var read = await store.ReadAsync(CancellationToken.None);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(filePath));
        var properties = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.True(read.Succeeded);
        Assert.Equal(settings, read.Value);
        Assert.Equal(new[] { "mode", "version" }, properties);
        Assert.Equal(expectedJsonMode, document.RootElement.GetProperty("mode").GetString());
        Assert.False(document.RootElement.TryGetProperty("customHttpProxyUri", out _));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ReadAsync_MapsMalformedInvalidAndFutureDocumentsToStableStatuses()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var future = new WindowsProxySettings(
            WindowsProxySettingsMigration.CurrentVersion + 1,
            WindowsProxyMode.SystemDefault,
            null);

        await File.WriteAllBytesAsync(filePath, "{ malformed"u8.ToArray());
        var malformed = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes("""
            {"version":1,"mode":1,"customHttpProxyUri":null}
            """));
        var invalid = await store.ReadAsync(CancellationToken.None);
        await File.WriteAllBytesAsync(filePath, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            future,
            TransDuck.Core.Contracts.V1.ContractJson.SerializerOptions)));
        var unsupported = await store.ReadAsync(CancellationToken.None);

        Assert.Equal(PersistenceStatus.InvalidData, malformed.Status);
        Assert.Equal(PersistenceStatus.InvalidData, invalid.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, unsupported.Status);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_RejectsInvalidAndFutureSettingsWithoutCreatingAFile()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var invalid = new WindowsProxySettings(
            WindowsProxySettingsMigration.CurrentVersion,
            WindowsProxyMode.CustomHttp,
            null);
        var future = new WindowsProxySettings(
            WindowsProxySettingsMigration.CurrentVersion + 1,
            WindowsProxyMode.SystemDefault,
            null);

        var invalidWrite = await store.WriteAsync(invalid, CancellationToken.None);
        var futureWrite = await store.WriteAsync(future, CancellationToken.None);

        Assert.Equal(PersistenceStatus.InvalidData, invalidWrite.Status);
        Assert.Equal(PersistenceStatus.UnsupportedVersion, futureWrite.Status);
        Assert.False(File.Exists(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Operations_ReturnCancelledBeforeAnyFilesystemMutation()
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var write = await store.WriteAsync(Custom("http://proxy.example.test:8080"), cancellation.Token);
        var read = await store.ReadAsync(cancellation.Token);

        Assert.Equal(PersistenceStatus.Cancelled, write.Status);
        Assert.Equal(PersistenceStatus.Cancelled, read.Status);
        Assert.False(File.Exists(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    private static JsonWindowsProxySettingsStore CreateStore(
        PersistenceTestDirectory temporary,
        out string filePath)
    {
        filePath = temporary.FilePath(JsonWindowsProxySettingsStore.FileName);
        var temporaryRoot = Path.GetFullPath(temporary.RootDirectory).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var applicationPath = Path.Combine(new WindowsDataPaths().RootDirectory, JsonWindowsProxySettingsStore.FileName);

        Assert.StartsWith(temporaryRoot, Path.GetFullPath(filePath));
        Assert.False(string.Equals(
            Path.GetFullPath(filePath),
            Path.GetFullPath(applicationPath),
            StringComparison.OrdinalIgnoreCase));
        return new JsonWindowsProxySettingsStore(filePath);
    }

    private static WindowsProxySettings Custom(string value) => new(
        WindowsProxySettingsMigration.CurrentVersion,
        WindowsProxyMode.CustomHttp,
        new Uri(value, UriKind.Absolute));
}
