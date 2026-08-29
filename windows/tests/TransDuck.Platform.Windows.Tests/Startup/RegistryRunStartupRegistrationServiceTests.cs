// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Platform.Windows.Startup;
using TransDuck.Platform.Windows.Tests.Persistence;

namespace TransDuck.Platform.Windows.Tests.Startup;

public sealed class RegistryRunStartupRegistrationServiceTests
{
    [Fact]
    public void CurrentEntry_EnableAndDisable_UsesOnlyTheOwnedValueAndQuotesTheExecutablePath()
    {
        using var temporary = new PersistenceTestDirectory();
        var executablePath = CreateExecutable(temporary, "current release", "TransDuck.exe");
        using var registry = new FakeRunRegistryBackend
        {
            Values =
            {
                ["OtherApplication"] = RunRegistryValue.String("\"C:\\Tools\\Other.exe\" --background"),
            },
        };
        using var service = CreateService(registry, executablePath);

        var initial = service.GetStatus();
        var enabled = service.Enable();
        var current = service.GetStatus();
        var disabled = service.Disable();

        Assert.Equal(StartupRegistrationStatus.Disabled, initial.Status);
        Assert.Equal(StartupRegistrationStatus.Enabled, enabled.Status);
        Assert.Equal(StartupRegistrationStatus.Enabled, current.Status);
        Assert.Equal(StartupRegistrationStatus.Disabled, disabled.Status);
        Assert.Equal('"' + executablePath + '"', registry.Writes[RegistryRunStartupRegistrationService.ValueName]);
        Assert.DoesNotContain(RegistryRunStartupRegistrationService.ValueName, registry.Values.Keys);
        Assert.Equal(
            RunRegistryValue.String("\"C:\\Tools\\Other.exe\" --background"),
            registry.Values["OtherApplication"]);
        Assert.All(registry.ReadNames, name => Assert.Equal(RegistryRunStartupRegistrationService.ValueName, name));
        Assert.All(registry.WriteNames, name => Assert.Equal(RegistryRunStartupRegistrationService.ValueName, name));
        Assert.All(registry.DeleteNames, name => Assert.Equal(RegistryRunStartupRegistrationService.ValueName, name));
    }

    [Fact]
    public void OwnedStaleEntry_EnableRepairsAndDisableDeletesOnlyTheOwnedValue()
    {
        using var temporary = new PersistenceTestDirectory();
        var executablePath = CreateExecutable(temporary, "current", "TransDuck.exe");
        var stalePath = Path.Combine(temporary.RootDirectory, "previous", "TransDuck.exe");
        using var repairRegistry = CreateRegistryWithOwnedStaleEntry(stalePath);
        using var repairService = CreateService(repairRegistry, executablePath);

        var stale = repairService.GetStatus();
        var repaired = repairService.Enable();

        Assert.Equal(StartupRegistrationStatus.Stale, stale.Status);
        Assert.Equal(StartupRegistrationStatus.Enabled, repaired.Status);
        Assert.Equal('"' + executablePath + '"', repairRegistry.Values[RegistryRunStartupRegistrationService.ValueName].StringValue);
        Assert.Empty(repairRegistry.DeleteNames);
        AssertUnrelatedValueIsPreserved(repairRegistry);

        using var deleteRegistry = CreateRegistryWithOwnedStaleEntry(stalePath);
        using var deleteService = CreateService(deleteRegistry, executablePath);

        var removed = deleteService.Disable();

        Assert.Equal(StartupRegistrationStatus.Disabled, removed.Status);
        Assert.DoesNotContain(RegistryRunStartupRegistrationService.ValueName, deleteRegistry.Values.Keys);
        Assert.Empty(deleteRegistry.WriteNames);
        AssertUnrelatedValueIsPreserved(deleteRegistry);
    }

    [Theory]
    [InlineData("\"C:\\Tools\\Other.exe\"")]
    [InlineData("\"C:\\Tools\\TransDuck.exe\" --unexpected")]
    [InlineData("not a quoted command")]
    public void UnknownStringEntry_IsConflictAndCannotBeReplacedOrDeleted(string existingValue)
    {
        using var temporary = new PersistenceTestDirectory();
        var executablePath = CreateExecutable(temporary, "current", "TransDuck.exe");
        using var registry = new FakeRunRegistryBackend
        {
            Values =
            {
                [RegistryRunStartupRegistrationService.ValueName] = RunRegistryValue.String(existingValue),
                ["OtherApplication"] = RunRegistryValue.String("\"C:\\Tools\\Other.exe\""),
            },
        };
        using var service = CreateService(registry, executablePath);

        var status = service.GetStatus();
        var enabled = service.Enable();
        var disabled = service.Disable();

        Assert.Equal(StartupRegistrationStatus.Conflict, status.Status);
        Assert.Equal(StartupRegistrationStatus.Conflict, enabled.Status);
        Assert.Equal(StartupRegistrationStatus.Conflict, disabled.Status);
        Assert.Equal(existingValue, registry.Values[RegistryRunStartupRegistrationService.ValueName].StringValue);
        Assert.Empty(registry.WriteNames);
        Assert.Empty(registry.DeleteNames);
        AssertUnrelatedValueIsPreserved(registry);
    }

    [Fact]
    public void NonStringEntry_IsConflictAndCannotBeReplacedOrDeleted()
    {
        using var temporary = new PersistenceTestDirectory();
        var executablePath = CreateExecutable(temporary, "current", "TransDuck.exe");
        using var registry = new FakeRunRegistryBackend
        {
            Values =
            {
                [RegistryRunStartupRegistrationService.ValueName] = RunRegistryValue.Other(),
            },
        };
        using var service = CreateService(registry, executablePath);

        Assert.Equal(StartupRegistrationStatus.Conflict, service.GetStatus().Status);
        Assert.Equal(StartupRegistrationStatus.Conflict, service.Enable().Status);
        Assert.Equal(StartupRegistrationStatus.Conflict, service.Disable().Status);
        Assert.Empty(registry.WriteNames);
        Assert.Empty(registry.DeleteNames);
    }

    [Fact]
    public void InvalidCurrentPaths_AreUnavailableWithoutAccessingTheRegistry()
    {
        using var temporary = new PersistenceTestDirectory();
        var nonExecutablePath = CreateExecutable(temporary, "invalid", "TransDuck.dll");
        var dotnetPath = CreateExecutable(temporary, "invalid", "dotnet.exe");
        var otherExecutablePath = CreateExecutable(temporary, "invalid", "Other.exe");
        var legacyExecutablePath = CreateExecutable(temporary, "invalid", "Easydict.App.exe");
        var missingPath = Path.Combine(temporary.RootDirectory, "missing", "TransDuck.exe");
        var overlongPath = CreateExecutableWithCommandLongerThan260Characters(temporary);
        var invalidPaths = new string?[]
        {
            null,
            string.Empty,
            "relative\\TransDuck.exe",
            nonExecutablePath,
            dotnetPath,
            otherExecutablePath,
            legacyExecutablePath,
            missingPath,
            overlongPath,
        };

        foreach (var invalidPath in invalidPaths)
        {
            using var registry = new FakeRunRegistryBackend();
            using var service = CreateService(registry, invalidPath);

            Assert.Equal(StartupRegistrationStatus.Unavailable, service.GetStatus().Status);
            Assert.Equal(StartupRegistrationStatus.Unavailable, service.Enable().Status);
            Assert.Equal(StartupRegistrationStatus.Unavailable, service.Disable().Status);
            Assert.Empty(registry.ReadNames);
            Assert.Empty(registry.WriteNames);
            Assert.Empty(registry.DeleteNames);
        }
    }

    [Fact]
    public void RegistryFailures_AreMappedToFailedWithoutTouchingUnrelatedValues()
    {
        using var temporary = new PersistenceTestDirectory();
        var executablePath = CreateExecutable(temporary, "current", "TransDuck.exe");

        using var readRegistry = new FakeRunRegistryBackend { ThrowOnRead = true };
        using var readService = CreateService(readRegistry, executablePath);
        Assert.Equal(StartupRegistrationStatus.Failed, readService.GetStatus().Status);

        using var writeRegistry = new FakeRunRegistryBackend { ThrowOnWrite = true };
        using var writeService = CreateService(writeRegistry, executablePath);
        Assert.Equal(StartupRegistrationStatus.Failed, writeService.Enable().Status);
        Assert.DoesNotContain(RegistryRunStartupRegistrationService.ValueName, writeRegistry.Values.Keys);

        using var deleteRegistry = new FakeRunRegistryBackend
        {
            ThrowOnDelete = true,
            Values =
            {
                [RegistryRunStartupRegistrationService.ValueName] = RunRegistryValue.String('"' + executablePath + '"'),
                ["OtherApplication"] = RunRegistryValue.String("\"C:\\Tools\\Other.exe\""),
            },
        };
        using var deleteService = CreateService(deleteRegistry, executablePath);
        Assert.Equal(StartupRegistrationStatus.Failed, deleteService.Disable().Status);
        Assert.Equal('"' + executablePath + '"', deleteRegistry.Values[RegistryRunStartupRegistrationService.ValueName].StringValue);
        AssertUnrelatedValueIsPreserved(deleteRegistry);
    }

    [Fact]
    public void StartupRegistrationStatus_RemainsClosedAndExposesOwnedSemantics()
    {
        Assert.Equal(
            new[]
            {
                StartupRegistrationStatus.Enabled,
                StartupRegistrationStatus.Disabled,
                StartupRegistrationStatus.Stale,
                StartupRegistrationStatus.Conflict,
                StartupRegistrationStatus.Unavailable,
                StartupRegistrationStatus.Failed,
            },
            Enum.GetValues<StartupRegistrationStatus>());
        Assert.True(StartupRegistrationResult.Enabled().IsEnabled);
        Assert.True(StartupRegistrationResult.Enabled().IsOwned);
        Assert.False(StartupRegistrationResult.Stale().IsEnabled);
        Assert.True(StartupRegistrationResult.Stale().IsOwned);
        Assert.False(StartupRegistrationResult.Conflict().IsOwned);
        Assert.False(StartupRegistrationResult.Unavailable().IsOwned);
    }

    [Fact]
    public void LegacyEasydictRunValue_RemainsIndependentFromTheTransDuckStartupEntry()
    {
        const string legacyValueName = "Easydict.Windows";
        const string legacyCommand = "\"C:\\Tools\\Easydict.App.exe\"";
        using var temporary = new PersistenceTestDirectory();
        var executablePath = CreateExecutable(temporary, "current", "TransDuck.exe");
        using var registry = new FakeRunRegistryBackend
        {
            Values =
            {
                [legacyValueName] = RunRegistryValue.String(legacyCommand),
            },
        };
        using var service = CreateService(registry, executablePath);

        var enabled = service.Enable();

        Assert.Equal("TransDuck.Windows", RegistryRunStartupRegistrationService.ValueName);
        Assert.NotEqual(legacyValueName, RegistryRunStartupRegistrationService.ValueName);
        Assert.Equal(StartupRegistrationStatus.Enabled, enabled.Status);
        Assert.Equal(legacyCommand, registry.Values[legacyValueName].StringValue);
        Assert.Equal('"' + executablePath + '"', registry.Values[RegistryRunStartupRegistrationService.ValueName].StringValue);
        Assert.DoesNotContain(legacyValueName, registry.ReadNames);
        Assert.DoesNotContain(legacyValueName, registry.WriteNames);
        Assert.DoesNotContain(legacyValueName, registry.DeleteNames);
    }

    private static RegistryRunStartupRegistrationService CreateService(
        IRunRegistryBackend registry,
        string? executablePath) => new(registry, () => executablePath);

    private static FakeRunRegistryBackend CreateRegistryWithOwnedStaleEntry(string stalePath) => new()
    {
        Values =
        {
            [RegistryRunStartupRegistrationService.ValueName] = RunRegistryValue.String('"' + stalePath + '"'),
            ["OtherApplication"] = RunRegistryValue.String("\"C:\\Tools\\Other.exe\""),
        },
    };

    private static string CreateExecutable(PersistenceTestDirectory temporary, string directoryName, string fileName)
    {
        var directory = temporary.DirectoryPath(directoryName);
        Directory.CreateDirectory(directory);
        var executablePath = Path.Combine(directory, fileName);
        File.WriteAllBytes(executablePath, [0]);
        return executablePath;
    }

    private static string CreateExecutableWithCommandLongerThan260Characters(PersistenceTestDirectory temporary)
    {
        const string executableFileName = "TransDuck.exe";
        const int targetPathLength = 259;
        var targetDirectoryLength = targetPathLength - executableFileName.Length - 1;
        var directory = temporary.RootDirectory;
        while (Path.Combine(directory, "startup").Length <= targetDirectoryLength)
        {
            directory = Path.Combine(directory, "startup");
        }

        var finalSegmentLength = targetDirectoryLength - directory.Length - 1;
        Assert.True(finalSegmentLength >= 0);
        if (finalSegmentLength > 0)
        {
            directory = Path.Combine(directory, new string('s', finalSegmentLength));
        }

        Assert.Equal(targetDirectoryLength, directory.Length);
        Directory.CreateDirectory(directory);
        var executablePath = Path.Combine(directory, executableFileName);
        File.WriteAllBytes(executablePath, [0]);

        Assert.True(File.Exists(executablePath));
        Assert.Equal(executableFileName, Path.GetFileName(executablePath));
        Assert.Equal(targetPathLength, executablePath.Length);
        Assert.Equal(261, ('"' + executablePath + '"').Length);
        return executablePath;
    }

    private static void AssertUnrelatedValueIsPreserved(FakeRunRegistryBackend registry) =>
        Assert.Equal(RunRegistryValue.String("\"C:\\Tools\\Other.exe\""), registry.Values["OtherApplication"]);

    private sealed class FakeRunRegistryBackend : IRunRegistryBackend
    {
        public Dictionary<string, RunRegistryValue> Values { get; } = new(StringComparer.Ordinal);

        public List<string> ReadNames { get; } = [];

        public List<string> WriteNames { get; } = [];

        public List<string> DeleteNames { get; } = [];

        public Dictionary<string, string> Writes { get; } = new(StringComparer.Ordinal);

        public bool ThrowOnRead { get; init; }

        public bool ThrowOnWrite { get; init; }

        public bool ThrowOnDelete { get; init; }

        public RunRegistryValue Read(string valueName)
        {
            ReadNames.Add(valueName);
            if (ThrowOnRead)
            {
                throw new InvalidOperationException("read failure");
            }

            return Values.TryGetValue(valueName, out var value) ? value : RunRegistryValue.Missing();
        }

        public void WriteString(string valueName, string value)
        {
            WriteNames.Add(valueName);
            if (ThrowOnWrite)
            {
                throw new InvalidOperationException("write failure");
            }

            Values[valueName] = RunRegistryValue.String(value);
            Writes[valueName] = value;
        }

        public void Delete(string valueName)
        {
            DeleteNames.Add(valueName);
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("delete failure");
            }

            Values.Remove(valueName);
        }

        public void Dispose()
        {
        }
    }
}
