// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json;

namespace TransDuck.Core.Tests.Contracts;

public sealed class ContractSchemaTests
{
    private const string Draft202012 = "https://json-schema.org/draft/2020-12/schema";

    [Theory]
    [MemberData(nameof(Schemas))]
    public void Schema_HasExpectedDialectIdentifierAndRequiredFields(
        string fileName,
        string identifier,
        string[] requiredFields)
    {
        using var document = JsonDocument.Parse(ContractFixturePaths.ReadSchema(fileName));
        var root = document.RootElement;

        Assert.Equal(Draft202012, root.GetProperty("$schema").GetString());
        Assert.Equal(identifier, root.GetProperty("$id").GetString());
        if (requiredFields.Length == 0)
        {
            Assert.False(root.TryGetProperty("required", out _));
            return;
        }

        var actualRequired = root.GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        Assert.Equal(requiredFields, actualRequired);
    }

    public static IEnumerable<object[]> Schemas =>
    [
        ["configuration.schema.json", "urn:transduck:contracts:v1:schemas:configuration", new[] { "schemaVersion", "version", "defaultProvider", "historyRetention" }],
        ["definitions.schema.json", "urn:transduck:contracts:v1:schemas:definitions", Array.Empty<string>()],
        ["history-entry.schema.json", "urn:transduck:contracts:v1:schemas:history-entry", new[] { "schemaVersion", "entryId", "createdAt", "request", "result" }],
        ["query-request.schema.json", "urn:transduck:contracts:v1:schemas:query-request", new[] { "schemaVersion", "requestId", "queryKind", "text", "sourceLanguage", "targetLanguage", "provider" }],
        ["query-result.schema.json", "urn:transduck:contracts:v1:schemas:query-result", new[] { "schemaVersion", "requestId", "queryKind", "provider", "terminalState", "sourceLanguage", "targetLanguage" }],
        ["stream-event.schema.json", "urn:transduck:contracts:v1:schemas:stream-event", new[] { "schemaVersion", "requestId", "sequence", "eventType" }],
    ];
}
