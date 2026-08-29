// Copyright (c) 2026 maywine. All rights reserved.

namespace TransDuck.Platform.Windows.Tests;

public sealed class ProductIdentitySourceTests
{
    [Fact]
    public void RuntimeTranslationSettings_UsesOnlyTheTransDuckEnvironmentVariableNamespace()
    {
        var source = ReadRepositoryFile(
            "windows",
            "src",
            "TransDuck.App",
            "Services",
            "RuntimeTranslationSettings.cs");

        Assert.Contains("\"TRANSDUCK_OPENAI_ENDPOINT\"", source, StringComparison.Ordinal);
        Assert.Contains("\"TRANSDUCK_OPENAI_MODEL\"", source, StringComparison.Ordinal);
        Assert.Contains("\"TRANSDUCK_OPENAI_API_KEY\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EASYDICT_OPENAI_", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DpapiCredentialStore_UsesOnlyTheTransDuckEntropyNamespace()
    {
        var source = ReadRepositoryFile(
            "windows",
            "src",
            "TransDuck.Platform.Windows",
            "Persistence",
            "DpapiCredentialStore.cs");

        Assert.Contains("\"TransDuck.DpapiCredentialStore.v1\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Easydict.DpapiCredentialStore.v1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PortableZipStaticChecks_UseOnlyTheTransDuckProductIdentity()
    {
        var package = ReadRepositoryFile("windows", "packaging", "Package-Zip.ps1");
        var audit = ReadRepositoryFile("windows", "packaging", "Test-Package-Zip.ps1");

        Assert.Contains("ArchiveFileName = 'TransDuck-Windows-x64.zip'", package, StringComparison.Ordinal);
        Assert.Contains("PayloadDirectoryName = 'TransDuck-Windows-x64'", package, StringComparison.Ordinal);
        Assert.Contains("'TransDuck.exe'", package, StringComparison.Ordinal);
        Assert.Contains("TransDuck-Windows-x64/TransDuck.exe", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("Easydict", package + audit, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRepositoryFile(params string[] relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine([directory.FullName, .. relativePath]);
                if (File.Exists(candidate))
                {
                    return File.ReadAllText(candidate);
                }
            }
        }

        throw new FileNotFoundException("The requested repository source file was not found from the test host path.");
    }
}
