// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Tests.Contracts;

public sealed class ContractFixtureManifestTests
{
    [Fact]
    public void Manifest_ListsEveryFixtureExactlyOnceWithExpectedMetadata()
    {
        var manifest = ContractFixturePaths.LoadManifest();
        var fixtureRoot = ContractFixturePaths.GetFixtureRoot();
        var paths = manifest.Fixtures.Select(fixture => fixture.Path).ToArray();
        var actualPaths = Directory.EnumerateFiles(fixtureRoot, "*.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(fixtureRoot, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => !string.Equals(path, "manifest.json", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(actualPaths, paths.OrderBy(path => path, StringComparer.Ordinal));
        Assert.Equal(12, manifest.Fixtures.Count(fixture => fixture.Expected == "valid"));
        Assert.Equal(5, manifest.Fixtures.Count(fixture => fixture.Expected == "invalid"));
        foreach (var fixture in manifest.Fixtures)
        {
            Assert.True(File.Exists(Path.Combine(fixtureRoot, fixture.Path)));
            Assert.Contains(fixture.DocumentType, new[]
            {
                "configuration",
                "historyEntry",
                "queryRequest",
                "queryResult",
                "streamEvent",
            });
            Assert.Contains(fixture.Expected, new[] { "valid", "invalid" });
            if (fixture.Expected == "valid")
            {
                Assert.Null(fixture.ErrorCategory);
            }
            else
            {
                Assert.True(Enum.TryParse<ContractValidationError>(fixture.ErrorCategory, ignoreCase: true, out _));
            }
        }
    }
}
