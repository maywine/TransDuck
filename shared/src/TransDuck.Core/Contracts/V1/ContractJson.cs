// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransDuck.Core.Contracts.V1;

/// <summary>
/// Marks a v1 document that validates its required fields after deserialization.
/// </summary>
public interface IContractDocument
{
    int SchemaVersion { get; }

    void Validate();
}

/// <summary>
/// Serializes v1 documents with forward-compatible unknown-field handling.
/// </summary>
public static class ContractJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

    public static string Serialize<TDocument>(TDocument document)
        where TDocument : IContractDocument
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Validate();
        return JsonSerializer.Serialize(document, SerializerOptions);
    }

    public static TDocument Deserialize<TDocument>(string json)
        where TDocument : IContractDocument
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            EnsureRequiredProperties<TDocument>(json);
            var document = JsonSerializer.Deserialize<TDocument>(json, SerializerOptions)
                ?? throw new ContractValidationException(
                    ContractValidationError.MissingRequired,
                    "Contract JSON does not contain a document.");
            document.Validate();
            return document;
        }
        catch (JsonException exception)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Contract JSON contains an invalid property type or enum value.",
                exception);
        }
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private static void EnsureRequiredProperties<TDocument>(string json)
        where TDocument : IContractDocument
    {
        using var document = JsonDocument.Parse(json);
        ValidateDocumentShape(typeof(TDocument), document.RootElement);
    }

    private static void ValidateDocumentShape(Type documentType, JsonElement root)
    {
        ContractValidation.RequireCondition(
            root.ValueKind == JsonValueKind.Object,
            ContractValidationError.InvalidValue,
            "Contract JSON must contain an object.");
        foreach (var propertyName in GetRequiredProperties(documentType))
        {
            ContractValidation.RequireCondition(
                root.TryGetProperty(propertyName, out _),
                ContractValidationError.MissingRequired,
                $"Missing required property: {propertyName}.");
        }

        if (documentType == typeof(QueryResult))
        {
            ValidateQueryResultTerminalShape(root);
        }
        else if (documentType == typeof(StreamEvent))
        {
            ValidateStreamEventTerminalShape(root);
        }
        else if (documentType == typeof(HistoryEntry))
        {
            ValidateDocumentShape(typeof(QueryRequest), GetRequiredObject(root, "request"));
            ValidateDocumentShape(typeof(QueryResult), GetRequiredObject(root, "result"));
        }
    }

    private static IReadOnlyList<string> GetRequiredProperties(Type documentType) =>
        documentType == typeof(QueryRequest)
            ? ["schemaVersion", "requestId", "queryKind", "text", "sourceLanguage", "targetLanguage", "provider"]
            : documentType == typeof(QueryResult)
                ? ["schemaVersion", "requestId", "queryKind", "provider", "terminalState", "sourceLanguage", "targetLanguage"]
                : documentType == typeof(StreamEvent)
                    ? ["schemaVersion", "requestId", "sequence", "eventType"]
                    : documentType == typeof(Configuration)
                        ? ["schemaVersion", "version", "defaultProvider", "historyRetention"]
                        : documentType == typeof(HistoryEntry)
                            ? ["schemaVersion", "entryId", "createdAt", "request", "result"]
                            : throw new ContractValidationException(
                                ContractValidationError.InvalidValue,
                                "Unsupported v1 contract document type.");

    private static void ValidateQueryResultTerminalShape(JsonElement root)
    {
        if (root.GetProperty("terminalState").ValueKind != JsonValueKind.String)
        {
            return;
        }

        switch (root.GetProperty("terminalState").GetString())
        {
            case "completed":
                RequirePresentNonNull(root, "result");
                RequireAbsent(root, "error");
                break;
            case "cancelled":
                RequireAbsent(root, "result");
                RequireAbsent(root, "error");
                break;
            case "failed":
                RequirePresentNonNull(root, "error");
                RequireAbsent(root, "result");
                break;
        }
    }

    private static void ValidateStreamEventTerminalShape(JsonElement root)
    {
        if (root.GetProperty("eventType").ValueKind != JsonValueKind.String)
        {
            return;
        }

        switch (root.GetProperty("eventType").GetString())
        {
            case "delta":
                RequirePresentNonNull(root, "text");
                RequireAbsent(root, "error");
                break;
            case "completed":
            case "cancelled":
                RequireAbsent(root, "text");
                RequireAbsent(root, "error");
                break;
            case "failed":
                RequirePresentNonNull(root, "error");
                RequireAbsent(root, "text");
                break;
        }
    }

    private static JsonElement GetRequiredObject(JsonElement root, string propertyName)
    {
        var value = root.GetProperty(propertyName);
        ContractValidation.RequireCondition(
            value.ValueKind == JsonValueKind.Object,
            ContractValidationError.InvalidValue,
            $"Invalid property value: {propertyName}.");
        return value;
    }

    private static void RequirePresentNonNull(JsonElement root, string propertyName)
    {
        ContractValidation.RequireCondition(
            root.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null,
            ContractValidationError.InvalidTerminalShape,
            $"Terminal shape requires non-null property: {propertyName}.");
    }

    private static void RequireAbsent(JsonElement root, string propertyName)
    {
        ContractValidation.RequireCondition(
            !root.TryGetProperty(propertyName, out _),
            ContractValidationError.InvalidTerminalShape,
            $"Terminal shape cannot contain property: {propertyName}.");
    }
}
