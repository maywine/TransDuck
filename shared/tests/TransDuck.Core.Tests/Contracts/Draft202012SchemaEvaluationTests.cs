// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json;
using Json.Schema;

namespace TransDuck.Core.Tests.Contracts;

public sealed class Draft202012SchemaEvaluationTests
{
    [Theory]
    [MemberData(nameof(ManifestFixtures))]
    public void ManifestFixture_EvaluatesWithItsDraft202012SchemaOffline(
        string relativePath,
        string documentType,
        bool expectedValid)
    {
        var schema = Assert.IsType<JsonSchema>(
            CreateRegistry().Get(new Uri(SchemaIdentifier(documentType))));
        using var document = JsonDocument.Parse(ContractFixturePaths.ReadFixture(relativePath));

        var result = schema.Evaluate(document.RootElement, EvaluationOptions());

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Schema_AllowsNullableProviderInstanceAndDictionaryEntriesButRejectsExplicitNullTerminals()
    {
        var registry = CreateRegistry();
        var queryRequest = Evaluate(registry, "queryRequest", """
            {"schemaVersion":1,"requestId":"request-schema-001","queryKind":"ocr","text":"synthetic","sourceLanguage":null,"targetLanguage":"en-US","provider":{"providerId":"local-ocr","instanceId":null}}
            """);
        var queryResult = Evaluate(registry, "queryResult", """
            {"schemaVersion":1,"requestId":"request-schema-002","queryKind":"ocr","provider":{"providerId":"local-ocr","instanceId":null},"terminalState":"completed","sourceLanguage":null,"targetLanguage":"en-US","result":{"text":"synthetic","dictionaryEntries":null}}
            """);
        var terminalResult = Evaluate(registry, "queryResult", """
            {"schemaVersion":1,"requestId":"request-schema-003","queryKind":"ocr","provider":{"providerId":"local-ocr"},"terminalState":"cancelled","sourceLanguage":null,"targetLanguage":"en-US","result":null}
            """);
        var terminalStream = Evaluate(registry, "streamEvent", """
            {"schemaVersion":1,"requestId":"request-schema-004","sequence":0,"eventType":"completed","text":null}
            """);
        var additive = Evaluate(registry, "queryRequest", """
            {"schemaVersion":1,"requestId":"request-schema-005","queryKind":"ocr","text":"synthetic","sourceLanguage":null,"targetLanguage":"en-US","provider":{"providerId":"local-ocr"},"futureOptionalHint":"accepted"}
            """);

        Assert.True(queryRequest.IsValid);
        Assert.True(queryResult.IsValid);
        Assert.False(terminalResult.IsValid);
        Assert.False(terminalStream.IsValid);
        Assert.True(additive.IsValid);
    }

    [Fact]
    public void HistorySchema_EnforcesDateTimeFormatWithoutNetworkResolution()
    {
        var result = Evaluate(CreateRegistry(), "historyEntry", """
            {"schemaVersion":1,"entryId":"history-schema-001","createdAt":"not-a-date","request":{"schemaVersion":1,"requestId":"request-schema-006","queryKind":"ocr","text":"synthetic","sourceLanguage":null,"targetLanguage":"en-US","provider":{"providerId":"local-ocr"}},"result":{"schemaVersion":1,"requestId":"request-schema-006","queryKind":"ocr","provider":{"providerId":"local-ocr"},"terminalState":"completed","sourceLanguage":null,"targetLanguage":"en-US","result":{"text":"synthetic"}}}
            """);

        Assert.False(result.IsValid);
    }

    public static IEnumerable<object[]> ManifestFixtures => ContractFixturePaths.LoadManifest().Fixtures
        .Select(fixture => new object[]
        {
            fixture.Path,
            fixture.DocumentType,
            string.Equals(fixture.Expected, "valid", StringComparison.Ordinal),
        });

    private static EvaluationResults Evaluate(SchemaRegistry registry, string documentType, string json)
    {
        var schema = Assert.IsType<JsonSchema>(registry.Get(new Uri(SchemaIdentifier(documentType))));
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(document.RootElement, EvaluationOptions());
    }

    private static SchemaRegistry CreateRegistry()
    {
        var registry = new SchemaRegistry
        {
            Fetch = (uri, _) => throw new InvalidOperationException($"Schema evaluation must not fetch over HTTP: {uri}"),
        };
        var options = new BuildOptions { SchemaRegistry = registry };
        foreach (var fileName in new[]
        {
            "definitions.schema.json",
            "query-request.schema.json",
            "query-result.schema.json",
            "stream-event.schema.json",
            "configuration.schema.json",
            "history-entry.schema.json",
        })
        {
            var schemaText = ContractFixturePaths.ReadSchema(fileName);
            using var document = JsonDocument.Parse(schemaText);
            var schemaUri = new Uri(document.RootElement.GetProperty("$id").GetString()!);
            var schema = JsonSchema.FromText(schemaText, options, schemaUri);
            registry.Register(schemaUri, schema);
        }

        return registry;
    }

    private static EvaluationOptions EvaluationOptions() => new()
    {
        RequireFormatValidation = true,
    };

    private static string SchemaIdentifier(string documentType) => documentType switch
    {
        "configuration" => "urn:transduck:contracts:v1:schemas:configuration",
        "historyEntry" => "urn:transduck:contracts:v1:schemas:history-entry",
        "queryRequest" => "urn:transduck:contracts:v1:schemas:query-request",
        "queryResult" => "urn:transduck:contracts:v1:schemas:query-result",
        "streamEvent" => "urn:transduck:contracts:v1:schemas:stream-event",
        _ => throw new ArgumentOutOfRangeException(nameof(documentType)),
    };
}
