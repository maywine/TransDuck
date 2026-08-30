// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Tests.Contracts;

public sealed class CoreAssemblyBoundaryTests
{
    [Fact]
    public void CoreAssembly_DoesNotReferencePlatformUiOrOcrDependencies()
    {
        var references = typeof(QueryRequest).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();
        var forbidden = references.Where(name =>
            name.Equals("PresentationCore", StringComparison.Ordinal) ||
            name.Equals("PresentationFramework", StringComparison.Ordinal) ||
            name.Equals("WindowsBase", StringComparison.Ordinal) ||
            name.Equals("System.Windows.Forms", StringComparison.Ordinal) ||
            name.Equals("Microsoft.Windows.SDK.NET", StringComparison.Ordinal) ||
            name.Equals("WinRT.Runtime", StringComparison.Ordinal) ||
            name.Equals("Tesseract", StringComparison.Ordinal));

        Assert.Empty(forbidden);
    }
}
