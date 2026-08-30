// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Tests.Contracts;

public sealed class ContractSerializationShapeTests
{
    [Fact]
    public void QueryRequest_WritesRequiredNullSourceLanguageAndOmitsNullProviderInstance()
    {
        var request = new QueryRequest(
            SchemaVersion: 1,
            RequestId: "request-shape-001",
            QueryKind: QueryKind.Ocr,
            Text: "synthetic text",
            SourceLanguage: null,
            TargetLanguage: "en-US",
            Provider: new ProviderDescriptor("local-ocr"));

        using var document = JsonDocument.Parse(ContractJson.Serialize(request));
        var root = document.RootElement;

        AssertRequiredNullSourceLanguage(root);
        Assert.False(root.GetProperty("provider").TryGetProperty("instanceId", out _));
    }

    [Fact]
    public void QueryResult_TerminalShapesOmitNullOptionalProperties()
    {
        var provider = new ProviderDescriptor("local-ocr");
        var completed = new QueryResult(
            1,
            "request-shape-002",
            QueryKind.Ocr,
            provider,
            QueryTerminalState.Completed,
            null,
            "en-US",
            Result: new QueryResultPayload("done"));
        var cancelled = completed with
        {
            TerminalState = QueryTerminalState.Cancelled,
            Result = null,
        };
        var failed = completed with
        {
            TerminalState = QueryTerminalState.Failed,
            Result = null,
            Error = new QueryError(QueryErrorCode.Timeout, "synthetic timeout", true),
        };

        using var completedDocument = JsonDocument.Parse(ContractJson.Serialize(completed));
        using var cancelledDocument = JsonDocument.Parse(ContractJson.Serialize(cancelled));
        using var failedDocument = JsonDocument.Parse(ContractJson.Serialize(failed));

        AssertRequiredNullSourceLanguage(completedDocument.RootElement);
        Assert.True(completedDocument.RootElement.TryGetProperty("result", out _));
        Assert.False(completedDocument.RootElement.TryGetProperty("error", out _));
        AssertRequiredNullSourceLanguage(cancelledDocument.RootElement);
        Assert.False(cancelledDocument.RootElement.TryGetProperty("result", out _));
        Assert.False(cancelledDocument.RootElement.TryGetProperty("error", out _));
        AssertRequiredNullSourceLanguage(failedDocument.RootElement);
        Assert.False(failedDocument.RootElement.TryGetProperty("result", out _));
        Assert.True(failedDocument.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void StreamEvent_TerminalShapesOmitNullTextAndError()
    {
        var completed = new StreamEvent(1, "request-shape-003", 1, StreamEventType.Completed);
        var cancelled = new StreamEvent(1, "request-shape-003", 2, StreamEventType.Cancelled);
        var failed = new StreamEvent(
            1,
            "request-shape-003",
            3,
            StreamEventType.Failed,
            Error: new QueryError(QueryErrorCode.Network, "synthetic network", true));

        using var completedDocument = JsonDocument.Parse(ContractJson.Serialize(completed));
        using var cancelledDocument = JsonDocument.Parse(ContractJson.Serialize(cancelled));
        using var failedDocument = JsonDocument.Parse(ContractJson.Serialize(failed));

        Assert.False(completedDocument.RootElement.TryGetProperty("text", out _));
        Assert.False(completedDocument.RootElement.TryGetProperty("error", out _));
        Assert.False(cancelledDocument.RootElement.TryGetProperty("text", out _));
        Assert.False(cancelledDocument.RootElement.TryGetProperty("error", out _));
        Assert.False(failedDocument.RootElement.TryGetProperty("text", out _));
        Assert.True(failedDocument.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void ValidFixture_RoundTripRetainsRequiredNullSourceLanguageShape()
    {
        var fixture = ContractFixturePaths.ReadFixture("valid", "query-request-null-source-language.json");
        var serialized = ContractJson.Serialize(ContractJson.Deserialize<QueryRequest>(fixture));

        using var document = JsonDocument.Parse(serialized);

        AssertRequiredNullSourceLanguage(document.RootElement);
        Assert.False(document.RootElement.GetProperty("provider").TryGetProperty("instanceId", out _));
    }

    private static void AssertRequiredNullSourceLanguage(JsonElement document)
    {
        Assert.True(document.TryGetProperty("sourceLanguage", out var sourceLanguage));
        Assert.Equal(JsonValueKind.Null, sourceLanguage.ValueKind);
    }
}
