// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json;
using TransDuck.Core.Persistence;
using TransDuck.Core.Contracts.V1;
using TransDuck.Platform.Windows.Persistence;

namespace TransDuck.Platform.Windows.Tests.Persistence;

public sealed class JsonLinesDiagnosticSinkTests
{
    [Fact]
    public async Task WriteAsync_RoundTripsOnlyAllowedStructuredDiagnosticFields()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var sink = new JsonLinesDiagnosticSink(filePath);

        var write = await sink.WriteAsync(PersistenceTestData.DiagnosticEvent(1), CancellationToken.None);
        var line = Assert.Single(await File.ReadAllLinesAsync(filePath));
        var diagnostic = JsonSerializer.Deserialize<DiagnosticEvent>(line, ContractJson.SerializerOptions);
        using var document = JsonDocument.Parse(line);

        Assert.Equal(PersistenceStatus.Succeeded, write.Status);
        Assert.NotNull(diagnostic);
        diagnostic!.Validate();
        Assert.Equal("providerSettingsWrite", document.RootElement.GetProperty("eventId").GetString());
        Assert.Equal(
            new[] { "durationMs", "eventId", "level", "outcome", "providerId", "requestId", "timestamp" },
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        AssertNoSensitiveDiagnosticContent(await File.ReadAllTextAsync(filePath), temporary.RootDirectory);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task ConcurrentWrites_RemainParseableAndDoNotLeakSensitiveCanaries()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var sink = new JsonLinesDiagnosticSink(filePath);
        var events = Enumerable.Range(0, 20).Select(PersistenceTestData.DiagnosticEvent).ToArray();

        var writes = await Task.WhenAll(events.Select(diagnosticEvent =>
            sink.WriteAsync(diagnosticEvent, CancellationToken.None)));
        var lines = await File.ReadAllLinesAsync(filePath);

        Assert.All(writes, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
        Assert.Equal(events.Length, lines.Length);
        foreach (var line in lines)
        {
            var diagnostic = JsonSerializer.Deserialize<DiagnosticEvent>(line, ContractJson.SerializerOptions);
            Assert.NotNull(diagnostic);
            diagnostic!.Validate();
        }

        AssertNoSensitiveDiagnosticContent(await File.ReadAllTextAsync(filePath), temporary.RootDirectory);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_SerializesClosedTranslationFailureEvent()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var sink = new JsonLinesDiagnosticSink(filePath);
        var diagnostic = new DiagnosticEvent(
            DateTimeOffset.UnixEpoch,
            DiagnosticLevel.Error,
            DiagnosticEventId.TranslationFailed,
            DiagnosticOutcome.Failed,
            RequestId: "request-translation-failed",
            ProviderId: "openai-compatible",
            ErrorCode: DiagnosticErrorCode.TranslationInvalidRequest);

        var result = await sink.WriteAsync(diagnostic, CancellationToken.None);
        using var document = JsonDocument.Parse(Assert.Single(await File.ReadAllLinesAsync(filePath)));

        Assert.Equal(PersistenceStatus.Succeeded, result.Status);
        Assert.Equal("translationFailed", document.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("error", document.RootElement.GetProperty("level").GetString());
        Assert.Equal("failed", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("translationInvalidRequest", document.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            new[] { "errorCode", "eventId", "level", "outcome", "providerId", "requestId", "timestamp" },
            document.RootElement.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        AssertNoSensitiveDiagnosticContent(await File.ReadAllTextAsync(filePath), temporary.RootDirectory);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_SerializesClosedHistoryDiagnosticsWithoutContentFields()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var sink = new JsonLinesDiagnosticSink(filePath);
        var corruptRead = new DiagnosticEvent(
            DateTimeOffset.UnixEpoch,
            DiagnosticLevel.Warning,
            DiagnosticEventId.HistoryRead,
            DiagnosticOutcome.Succeeded,
            ErrorCode: DiagnosticErrorCode.CorruptData);
        var emptyClear = new DiagnosticEvent(
            DateTimeOffset.UnixEpoch.AddSeconds(1),
            DiagnosticLevel.Information,
            DiagnosticEventId.HistoryClear,
            DiagnosticOutcome.NotFound);

        var corruptReadWrite = await sink.WriteAsync(corruptRead, CancellationToken.None);
        var emptyClearWrite = await sink.WriteAsync(emptyClear, CancellationToken.None);
        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.Equal(2, lines.Length);
        using var corruptReadDocument = JsonDocument.Parse(lines[0]);
        using var emptyClearDocument = JsonDocument.Parse(lines[1]);

        Assert.Equal(PersistenceStatus.Succeeded, corruptReadWrite.Status);
        Assert.Equal(PersistenceStatus.Succeeded, emptyClearWrite.Status);
        Assert.Equal("historyRead", corruptReadDocument.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("warning", corruptReadDocument.RootElement.GetProperty("level").GetString());
        Assert.Equal("succeeded", corruptReadDocument.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("corruptData", corruptReadDocument.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            new[] { "errorCode", "eventId", "level", "outcome", "timestamp" },
            corruptReadDocument.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("historyClear", emptyClearDocument.RootElement.GetProperty("eventId").GetString());
        Assert.Equal("information", emptyClearDocument.RootElement.GetProperty("level").GetString());
        Assert.Equal("notFound", emptyClearDocument.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            new[] { "eventId", "level", "outcome", "timestamp" },
            emptyClearDocument.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        AssertNoSensitiveDiagnosticContent(await File.ReadAllTextAsync(filePath), temporary.RootDirectory);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_SerializesClosedHotkeyDiagnosticsWithoutSensitiveFields()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var sink = new JsonLinesDiagnosticSink(filePath);
        var diagnostics = new[]
        {
            new DiagnosticEvent(
                DateTimeOffset.UnixEpoch,
                DiagnosticLevel.Information,
                DiagnosticEventId.HotkeySettingsRead,
                DiagnosticOutcome.NotFound),
            new DiagnosticEvent(
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                DiagnosticLevel.Information,
                DiagnosticEventId.HotkeySettingsWrite,
                DiagnosticOutcome.Succeeded),
            new DiagnosticEvent(
                DateTimeOffset.UnixEpoch.AddSeconds(2),
                DiagnosticLevel.Error,
                DiagnosticEventId.HotkeyRegistration,
                DiagnosticOutcome.Failed,
                ErrorCode: DiagnosticErrorCode.HotkeyConflict),
            new DiagnosticEvent(
                DateTimeOffset.UnixEpoch.AddSeconds(3),
                DiagnosticLevel.Error,
                DiagnosticEventId.HotkeyRegistration,
                DiagnosticOutcome.Failed,
                ErrorCode: DiagnosticErrorCode.HotkeyRegistrationFailure),
        };

        var writes = new List<PersistenceResult>();
        foreach (var diagnostic in diagnostics)
        {
            writes.Add(await sink.WriteAsync(diagnostic, CancellationToken.None));
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.All(writes, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
        Assert.Equal(diagnostics.Length, lines.Length);
        AssertHotkeyDiagnostic(lines[0], "hotkeySettingsRead", "information", "notFound", null);
        AssertHotkeyDiagnostic(lines[1], "hotkeySettingsWrite", "information", "succeeded", null);
        AssertHotkeyDiagnostic(lines[2], "hotkeyRegistration", "error", "failed", "hotkeyConflict");
        AssertHotkeyDiagnostic(lines[3], "hotkeyRegistration", "error", "failed", "hotkeyRegistrationFailure");
        AssertNoSensitiveDiagnosticContent(await File.ReadAllTextAsync(filePath), temporary.RootDirectory);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_SerializesClosedProxyDiagnosticsWithoutProxyConfigurationFields()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var sink = new JsonLinesDiagnosticSink(filePath);
        var diagnostics = new[]
        {
            new DiagnosticEvent(
                DateTimeOffset.UnixEpoch,
                DiagnosticLevel.Information,
                DiagnosticEventId.ProxySettingsRead,
                DiagnosticOutcome.NotFound),
            new DiagnosticEvent(
                DateTimeOffset.UnixEpoch.AddSeconds(1),
                DiagnosticLevel.Information,
                DiagnosticEventId.ProxySettingsWrite,
                DiagnosticOutcome.Succeeded),
            new DiagnosticEvent(
                DateTimeOffset.UnixEpoch.AddSeconds(2),
                DiagnosticLevel.Error,
                DiagnosticEventId.ProxySettingsWrite,
                DiagnosticOutcome.Failed,
                ErrorCode: DiagnosticErrorCode.InvalidData),
        };

        var writes = new List<PersistenceResult>();
        foreach (var diagnostic in diagnostics)
        {
            writes.Add(await sink.WriteAsync(diagnostic, CancellationToken.None));
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        Assert.All(writes, result => Assert.Equal(PersistenceStatus.Succeeded, result.Status));
        Assert.Equal(diagnostics.Length, lines.Length);
        AssertProxyDiagnostic(lines[0], "proxySettingsRead", "information", "notFound", null);
        AssertProxyDiagnostic(lines[1], "proxySettingsWrite", "information", "succeeded", null);
        AssertProxyDiagnostic(lines[2], "proxySettingsWrite", "error", "failed", "invalidData");
        AssertNoSensitiveDiagnosticContent(await File.ReadAllTextAsync(filePath), temporary.RootDirectory);
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task WriteAsync_RejectsUndefinedDiagnosticEnumMembersWithoutWriting()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var sink = new JsonLinesDiagnosticSink(filePath);
        var timestamp = DateTimeOffset.UnixEpoch;
        var invalidEvents = new[]
        {
            new DiagnosticEvent(
                timestamp,
                (DiagnosticLevel)999,
                DiagnosticEventId.HistoryAppend,
                DiagnosticOutcome.Failed),
            new DiagnosticEvent(
                timestamp,
                DiagnosticLevel.Error,
                (DiagnosticEventId)999,
                DiagnosticOutcome.Failed),
            new DiagnosticEvent(
                timestamp,
                DiagnosticLevel.Error,
                DiagnosticEventId.HistoryAppend,
                (DiagnosticOutcome)999),
            new DiagnosticEvent(
                timestamp,
                DiagnosticLevel.Error,
                DiagnosticEventId.HistoryAppend,
                DiagnosticOutcome.Failed,
                ErrorCode: (DiagnosticErrorCode)999),
        };

        var results = await Task.WhenAll(invalidEvents.Select(diagnosticEvent =>
            sink.WriteAsync(diagnosticEvent, CancellationToken.None)));

        Assert.All(results, result => Assert.Equal(PersistenceStatus.InvalidData, result.Status));
        Assert.False(File.Exists(filePath));
        temporary.AssertNoTemporaryFiles();
    }

    [Fact]
    public async Task Operations_DistinguishPreCancellationFromDisposedStateAndDisposeRace()
    {
        using var temporary = new PersistenceTestDirectory();
        var filePath = temporary.FilePath("diagnostics.jsonl");
        using var cancellableSink = new JsonLinesDiagnosticSink(filePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = await cancellableSink.WriteAsync(PersistenceTestData.DiagnosticEvent(1), cancellation.Token);

        Assert.Equal(PersistenceStatus.Cancelled, cancelled.Status);

        var raceSink = new JsonLinesDiagnosticSink(filePath);
        var operation = raceSink.WriteAsync(PersistenceTestData.DiagnosticEvent(2), CancellationToken.None);
        raceSink.Dispose();
        var raced = await operation;
        var afterDispose = await raceSink.WriteAsync(PersistenceTestData.DiagnosticEvent(3), CancellationToken.None);

        Assert.NotEqual(PersistenceStatus.Cancelled, raced.Status);
        Assert.Equal(PersistenceStatus.IoFailure, afterDispose.Status);
        temporary.AssertNoTemporaryFiles();
    }

    private static void AssertNoSensitiveDiagnosticContent(string content, string temporaryRoot)
    {
        Assert.False(content.Contains("APIKEY_CANARY", StringComparison.Ordinal));
        Assert.False(content.Contains("QUERY_CANARY", StringComparison.Ordinal));
        Assert.False(content.Contains("CLIPBOARD_CANARY", StringComparison.Ordinal));
        Assert.False(content.Contains(temporaryRoot, StringComparison.Ordinal));
        Assert.False(content.Contains("Exception", StringComparison.OrdinalIgnoreCase));
        Assert.False(content.Contains("query", StringComparison.OrdinalIgnoreCase));
        Assert.False(content.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
        Assert.False(content.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.False(content.Contains("message", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertHotkeyDiagnostic(
        string line,
        string eventId,
        string level,
        string outcome,
        string? errorCode)
    {
        using var document = JsonDocument.Parse(line);

        Assert.Equal(eventId, document.RootElement.GetProperty("eventId").GetString());
        Assert.Equal(level, document.RootElement.GetProperty("level").GetString());
        Assert.Equal(outcome, document.RootElement.GetProperty("outcome").GetString());
        if (errorCode is not null)
        {
            Assert.Equal(errorCode, document.RootElement.GetProperty("errorCode").GetString());
        }

        Assert.Equal(
            errorCode is null
                ? new[] { "eventId", "level", "outcome", "timestamp" }
                : new[] { "errorCode", "eventId", "level", "outcome", "timestamp" },
            document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static void AssertProxyDiagnostic(
        string line,
        string eventId,
        string level,
        string outcome,
        string? errorCode)
    {
        using var document = JsonDocument.Parse(line);

        Assert.Equal(eventId, document.RootElement.GetProperty("eventId").GetString());
        Assert.Equal(level, document.RootElement.GetProperty("level").GetString());
        Assert.Equal(outcome, document.RootElement.GetProperty("outcome").GetString());
        if (errorCode is not null)
        {
            Assert.Equal(errorCode, document.RootElement.GetProperty("errorCode").GetString());
        }

        Assert.Equal(
            errorCode is null
                ? new[] { "eventId", "level", "outcome", "timestamp" }
                : new[] { "errorCode", "eventId", "level", "outcome", "timestamp" },
            document.RootElement.EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }
}
