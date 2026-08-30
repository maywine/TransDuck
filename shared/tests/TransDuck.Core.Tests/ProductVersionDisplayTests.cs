// Copyright (c) 2026 maywine. All rights reserved.

using System.Reflection;
using System.Reflection.Emit;
using TransDuck.Core;

namespace TransDuck.Core.Tests;

public sealed class ProductVersionDisplayTests
{
    [Theory]
    [InlineData("1.2.3+build.42", "v1.2.3")]
    [InlineData("1.2.3-rc.1+build.42", "v1.2.3-rc.1")]
    [InlineData("v1.2.3+build.42", "v1.2.3")]
    [InlineData("1.2.3+001", "v1.2.3")]
    public void FromAssembly_PrefersInformationalVersionWithoutBuildMetadata(
        string informationalVersion,
        string expected)
    {
        var assembly = CreateAssembly(new Version(9, 8, 7, 6), informationalVersion);

        var actual = ProductVersionDisplay.FromAssembly(assembly);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+build.42")]
    [InlineData("not-a-version")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3+build..42")]
    [InlineData("1.2.3+build_42")]
    [InlineData("1.2.3+build+42")]
    public void FromAssembly_InvalidInformationalVersionFallsBackToAssemblyVersion(
        string informationalVersion)
    {
        var assembly = CreateAssembly(new Version(4, 5, 6, 7), informationalVersion);

        var actual = ProductVersionDisplay.FromAssembly(assembly);

        Assert.Equal("v4.5.6", actual);
    }

    [Fact]
    public void FromAssembly_MissingInformationalVersionUsesThreePartAssemblyVersion()
    {
        var assembly = CreateAssembly(new Version(4, 5, 6, 7), informationalVersion: null);

        var actual = ProductVersionDisplay.FromAssembly(assembly);

        Assert.Equal("v4.5.6", actual);
    }

    [Fact]
    public void FromAssembly_TwoPartAssemblyVersionPadsThePatchSegment()
    {
        var assembly = CreateAssembly(new Version(4, 5), informationalVersion: null);

        var actual = ProductVersionDisplay.FromAssembly(assembly);

        Assert.Equal("v4.5.0", actual);
    }

    [Fact]
    public void FromAssembly_NullAssemblyUsesStableFallback()
    {
        var actual = ProductVersionDisplay.FromAssembly(null);

        Assert.Equal("v0.0.0", actual);
    }

    [Fact]
    public void FromAssembly_CurrentCoreAssemblyAlwaysReturnsUserFacingVersionText()
    {
        var actual = ProductVersionDisplay.FromAssembly(typeof(ProductVersionDisplay).Assembly);

        Assert.StartsWith("v", actual, StringComparison.Ordinal);
        Assert.DoesNotContain('+', actual);
    }

    private static Assembly CreateAssembly(Version version, string? informationalVersion)
    {
        var assemblyName = new AssemblyName("ProductVersionDisplayTests." + Guid.NewGuid().ToString("N"))
        {
            Version = version,
        };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        if (informationalVersion is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informationalVersion]));
        }

        return assembly;
    }
}
