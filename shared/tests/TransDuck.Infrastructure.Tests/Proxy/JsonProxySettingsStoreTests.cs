// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using System.Text.Json;
using TransDuck.Core.Persistence;
using TransDuck.Infrastructure.Persistence;
using TransDuck.Infrastructure.Proxy;
using TransDuck.Infrastructure.Tests.Persistence;

namespace TransDuck.Infrastructure.Tests.Proxy;

public sealed class JsonProxySettingsStoreTests
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
    [InlineData(ProxyMode.SystemDefault, "systemDefault")]
    [InlineData(ProxyMode.Disabled, "disabled")]
    public async Task WriteAsync_RoundTripsNonCustomModesWithoutSerializingANullProxyUri(
        ProxyMode mode,
        string expectedJsonMode)
    {
        using var temporary = new PersistenceTestDirectory();
        using var store = CreateStore(temporary, out var filePath);
        var settings = new ProxySettings(
            ProxySettingsMigration.CurrentVersion,
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
        var future = new ProxySettings(
            ProxySettingsMigration.CurrentVersion + 1,
            ProxyMode.SystemDefault,
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
        var invalid = new ProxySettings(
            ProxySettingsMigration.CurrentVersion,
            ProxyMode.CustomHttp,
            null);
        var future = new ProxySettings(
            ProxySettingsMigration.CurrentVersion + 1,
            ProxyMode.SystemDefault,
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

    private static JsonProxySettingsStore CreateStore(
        PersistenceTestDirectory temporary,
        out string filePath)
    {
        filePath = temporary.FilePath(JsonProxySettingsStore.FileName);
        var temporaryRoot = Path.GetFullPath(temporary.RootDirectory).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        Assert.StartsWith(temporaryRoot, Path.GetFullPath(filePath));
        return new JsonProxySettingsStore(filePath);
    }

    private static ProxySettings Custom(string value) => new(
        ProxySettingsMigration.CurrentVersion,
        ProxyMode.CustomHttp,
        new Uri(value, UriKind.Absolute));
}
