// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Persistence;

namespace TransDuck.Core.Tests.Persistence;

public sealed class PersistenceAssemblyBoundaryTests
{
    [Fact]
    public void CorePersistenceAssembly_HasNoPlatformUiCryptoOrOcrReferences()
    {
        var references = typeof(CredentialSecret).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);
        var forbidden = references.Where(name =>
            name.Equals("PresentationCore", StringComparison.Ordinal) ||
            name.Equals("PresentationFramework", StringComparison.Ordinal) ||
            name.Equals("WindowsBase", StringComparison.Ordinal) ||
            name.Equals("Microsoft.Windows.SDK.NET", StringComparison.Ordinal) ||
            name.Equals("WinRT.Runtime", StringComparison.Ordinal) ||
            name.Equals("Tesseract", StringComparison.Ordinal) ||
            name.Equals("System.Security.Cryptography.ProtectedData", StringComparison.Ordinal));

        Assert.Empty(forbidden);
    }
}
