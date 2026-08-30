// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Tests.Contracts;

public sealed class ContractJsonFixtureTests
{
    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void ValidFixture_DeserializesValidatesAndRoundTrips(ContractFixtureKind kind, string relativePath)
    {
        var serialized = DeserializeAndSerialize(kind, ContractFixturePaths.ReadFixture(relativePath));

        var repeated = DeserializeAndSerialize(kind, serialized);

        Assert.Equal(serialized, repeated);
    }

    [Fact]
    public void QueryRequest_UnknownAdditiveFieldIsAcceptedAndNotReemitted()
    {
        var fixture = ContractFixturePaths.ReadFixture("valid", "query-request-unknown-additive-field.json");

        var request = ContractJson.Deserialize<QueryRequest>(fixture);
        var serialized = ContractJson.Serialize(request);

        Assert.Equal("request-synthetic-003", request.RequestId);
        Assert.Equal(QueryKind.Ocr, request.QueryKind);
        Assert.False(serialized.Contains("futureOptionalHint", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void InvalidFixture_IsRejectedWithStableErrorCategory(
        ContractFixtureKind kind,
        string relativePath,
        ContractValidationError expectedError)
    {
        var fixture = ContractFixturePaths.ReadFixture(relativePath);

        var exception = Assert.Throws<ContractValidationException>(() => DeserializeAndSerialize(kind, fixture));

        Assert.Equal(expectedError, exception.Error);
    }

    [Fact]
    public void TerminalDocuments_RejectIllegalPayloadShapes()
    {
        var provider = new ProviderDescriptor("local-ocr");
        var result = new QueryResult(
            SchemaVersion: 1,
            RequestId: "request-terminal-001",
            QueryKind: QueryKind.Ocr,
            Provider: provider,
            TerminalState: QueryTerminalState.Cancelled,
            SourceLanguage: null,
            TargetLanguage: "en-US",
            Result: new QueryResultPayload("unexpected"));
        var completed = new StreamEvent(
            SchemaVersion: 1,
            RequestId: "request-terminal-001",
            Sequence: 1,
            EventType: StreamEventType.Completed,
            Text: "unexpected");
        var failed = new StreamEvent(
            SchemaVersion: 1,
            RequestId: "request-terminal-001",
            Sequence: 2,
            EventType: StreamEventType.Failed);

        Assert.Equal(
            ContractValidationError.InvalidTerminalShape,
            Assert.Throws<ContractValidationException>(result.Validate).Error);
        Assert.Equal(
            ContractValidationError.InvalidTerminalShape,
            Assert.Throws<ContractValidationException>(completed.Validate).Error);
        Assert.Equal(
            ContractValidationError.InvalidTerminalShape,
            Assert.Throws<ContractValidationException>(failed.Validate).Error);
    }

    [Fact]
    public void HistoryEntry_DefaultCreatedAt_ValidatesWhenTheRequiredWireKeyIsPresent()
    {
        var provider = new ProviderDescriptor("local-ocr");
        var request = new QueryRequest(
            1,
            "request-default-date-001",
            QueryKind.Ocr,
            "synthetic text",
            null,
            "en-US",
            provider);
        var result = new QueryResult(
            1,
            request.RequestId,
            request.QueryKind,
            provider,
            QueryTerminalState.Completed,
            null,
            "en-US",
            new QueryResultPayload("synthetic result"));
        var entry = new HistoryEntry(1, "history-default-date-001", default, request, result);

        entry.Validate();
    }

    [Fact]
    public void QueryResultPayload_RejectsEmptyTextAsInvalidValue()
    {
        var payload = new QueryResultPayload(string.Empty);

        var exception = Assert.Throws<ContractValidationException>(payload.Validate);

        Assert.Equal(ContractValidationError.InvalidValue, exception.Error);
    }

    public static IEnumerable<object[]> ValidFixtures => ContractFixturePaths.LoadManifest().Fixtures
        .Where(fixture => string.Equals(fixture.Expected, "valid", StringComparison.Ordinal))
        .Select(fixture => new object[] { ParseKind(fixture.DocumentType), fixture.Path });

    public static IEnumerable<object[]> InvalidFixtures => ContractFixturePaths.LoadManifest().Fixtures
        .Where(fixture => string.Equals(fixture.Expected, "invalid", StringComparison.Ordinal))
        .Select(fixture => new object[]
        {
            ParseKind(fixture.DocumentType),
            fixture.Path,
            ParseError(fixture.ErrorCategory),
        });

    private static string DeserializeAndSerialize(ContractFixtureKind kind, string json) => kind switch
    {
        ContractFixtureKind.Configuration => RoundTrip<Configuration>(json),
        ContractFixtureKind.HistoryEntry => RoundTrip<HistoryEntry>(json),
        ContractFixtureKind.QueryRequest => RoundTrip<QueryRequest>(json),
        ContractFixtureKind.QueryResult => RoundTrip<QueryResult>(json),
        ContractFixtureKind.StreamEvent => RoundTrip<StreamEvent>(json),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string RoundTrip<TDocument>(string json)
        where TDocument : IContractDocument =>
        ContractJson.Serialize(ContractJson.Deserialize<TDocument>(json));

    private static ContractFixtureKind ParseKind(string documentType) => documentType switch
    {
        "configuration" => ContractFixtureKind.Configuration,
        "historyEntry" => ContractFixtureKind.HistoryEntry,
        "queryRequest" => ContractFixtureKind.QueryRequest,
        "queryResult" => ContractFixtureKind.QueryResult,
        "streamEvent" => ContractFixtureKind.StreamEvent,
        _ => throw new InvalidOperationException($"Unsupported fixture document type '{documentType}'."),
    };

    private static ContractValidationError ParseError(string? errorCategory) =>
        Enum.TryParse<ContractValidationError>(errorCategory, ignoreCase: true, out var error)
            ? error
            : throw new InvalidOperationException($"Unsupported fixture error category '{errorCategory}'.");
}

public enum ContractFixtureKind
{
    Configuration,
    HistoryEntry,
    QueryRequest,
    QueryResult,
    StreamEvent,
}
