using TransDuck.Infrastructure.Proxy;

namespace TransDuck.Infrastructure.Tests;

public sealed class InfrastructureAssemblyBoundaryTests
{
    [Fact]
    public void Infrastructure_DoesNotReferencePlatformUiNativeOrCredentialAssemblies()
    {
        var references = typeof(ProxySettings).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .ToArray();
        var forbidden = references.Where(name =>
            name.StartsWith("Avalonia", StringComparison.Ordinal) ||
            name.Equals("PresentationCore", StringComparison.Ordinal) ||
            name.Equals("PresentationFramework", StringComparison.Ordinal) ||
            name.Equals("Microsoft.Windows.SDK.NET", StringComparison.Ordinal) ||
            name.Equals("SharpHook", StringComparison.Ordinal) ||
            name.Equals("Tesseract", StringComparison.Ordinal) ||
            name.Equals("System.Security.Cryptography.ProtectedData", StringComparison.Ordinal));

        Assert.Empty(forbidden);
    }
}
