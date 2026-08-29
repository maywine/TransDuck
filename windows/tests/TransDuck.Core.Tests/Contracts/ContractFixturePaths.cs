// Copyright (c) 2026 maywine. All rights reserved.

using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransDuck.Core.Tests.Contracts;

internal static class ContractFixturePaths
{
    public static string ReadFixture(string group, string fileName) =>
        File.ReadAllText(Path.Combine(GetRepositoryRoot(), "contracts", "v1", "fixtures", group, fileName));

    public static string ReadFixture(string relativePath) =>
        File.ReadAllText(Path.Combine(GetFixtureRoot(), relativePath));

    public static string ReadSchema(string fileName) =>
        File.ReadAllText(Path.Combine(GetRepositoryRoot(), "contracts", "v1", "schemas", fileName));

    public static ContractFixtureManifest LoadManifest()
    {
        var path = Path.Combine(GetFixtureRoot(), "manifest.json");
        return JsonSerializer.Deserialize<ContractFixtureManifest>(File.ReadAllText(path)) ??
            throw new InvalidOperationException("The contract fixture manifest is empty.");
    }

    public static string GetFixtureRoot() =>
        Path.Combine(GetRepositoryRoot(), "contracts", "v1", "fixtures");

    private static string GetRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "contracts", "v1", "README.md")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException("The contracts/v1 repository directory was not found.");
    }
}

internal sealed record ContractFixtureManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("fixtures")] IReadOnlyList<ContractFixtureManifestEntry> Fixtures);

internal sealed record ContractFixtureManifestEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("expected")] string Expected,
    [property: JsonPropertyName("errorCategory")] string? ErrorCategory = null);
