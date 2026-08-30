// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.Infrastructure.Tests.Persistence;

internal static class PersistenceTestData
{
    public static Configuration Configuration(int maxEntries = 100, int maxAgeDays = 30) => new(
        SchemaVersion: 1,
        Version: ConfigurationMigration.CurrentVersion,
        DefaultProvider: new ProviderDescriptor("local-ocr"),
        HistoryRetention: new HistoryRetention(maxEntries, maxAgeDays));

    public static HistoryEntry HistoryEntry(
        string entryId,
        DateTimeOffset createdAt,
        string text = "synthetic history text")
    {
        var requestId = "request-" + entryId;
        var provider = new ProviderDescriptor("local-ocr");
        var request = new QueryRequest(
            1,
            requestId,
            QueryKind.Ocr,
            text,
            null,
            "en-US",
            provider);
        var result = new QueryResult(
            1,
            requestId,
            QueryKind.Ocr,
            provider,
            QueryTerminalState.Completed,
            null,
            "en-US",
            new QueryResultPayload(text));
        return new HistoryEntry(1, entryId, createdAt, request, result);
    }

    public static DiagnosticEvent DiagnosticEvent(int sequence) => new(
        Timestamp: DateTimeOffset.UnixEpoch.AddSeconds(sequence),
        Level: DiagnosticLevel.Information,
        EventId: DiagnosticEventId.ProviderSettingsWrite,
        Outcome: DiagnosticOutcome.Succeeded,
        RequestId: $"request-diagnostic-{sequence:D2}",
        ProviderId: "local-ocr",
        DurationMs: sequence);
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
