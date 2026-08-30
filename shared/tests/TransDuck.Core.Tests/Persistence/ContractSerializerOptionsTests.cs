// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json;
using System.Text.Json.Serialization;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Tests.Persistence;

public sealed class ContractSerializerOptionsTests
{
    [Fact]
    public void SerializerOptions_AreImmutableAndPreserveRoundTripBehavior()
    {
        var options = ContractJson.SerializerOptions;
        var fixture = ContractFixtureJson();

        Assert.Throws<InvalidOperationException>(() => options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);
        Assert.Throws<InvalidOperationException>(() => options.Converters.Add(new JsonStringEnumConverter()));

        var once = ContractJson.Serialize(ContractJson.Deserialize<QueryRequest>(fixture));
        var twice = ContractJson.Serialize(ContractJson.Deserialize<QueryRequest>(once));

        Assert.Equal(once, twice);
    }

    private static string ContractFixtureJson() =>
        """
        {
          "schemaVersion": 1,
          "requestId": "request-options-001",
          "queryKind": "translation",
          "text": "synthetic text",
          "sourceLanguage": null,
          "targetLanguage": "zh-Hans",
          "provider": { "providerId": "local-ocr" }
        }
        """;
}
