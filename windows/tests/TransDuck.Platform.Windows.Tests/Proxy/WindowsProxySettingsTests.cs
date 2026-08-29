// Copyright (c) 2026 maywine. All rights reserved.

using TransDuck.Core.Contracts.V1;
using TransDuck.Platform.Windows.Proxy;

namespace TransDuck.Platform.Windows.Tests.Proxy;

public sealed class WindowsProxySettingsTests
{
    [Fact]
    public void Default_UsesCurrentSystemDefaultWithoutAProxyUri()
    {
        var settings = WindowsProxySettings.Default;

        settings.Validate();

        Assert.Equal(WindowsProxySettingsMigration.CurrentVersion, settings.Version);
        Assert.Equal(WindowsProxyMode.SystemDefault, settings.Mode);
        Assert.Null(settings.CustomHttpProxyUri);
    }

    [Theory]
    [InlineData("http://proxy.example.test:1")]
    [InlineData("http://proxy.example.test:65535/")]
    [InlineData("http://[::1]:8080")]
    public void Validate_CustomHttpAcceptsOnlyCredentialFreeAuthorityWithAnExplicitBoundaryPort(string value)
    {
        var proxyUri = new Uri(value, UriKind.Absolute);
        var settings = Settings(WindowsProxyMode.CustomHttp, proxyUri);

        settings.Validate();

        Assert.True(WindowsProxySettings.IsValidCustomHttpProxyUri(proxyUri));
    }

    [Theory]
    [InlineData("http://proxy.example.test")]
    [InlineData("https://proxy.example.test:8080")]
    [InlineData("http://user:password@proxy.example.test:8080")]
    [InlineData("http://proxy@example.test:8080")]
    [InlineData("http://proxy.example.test:8080/path")]
    [InlineData("http://proxy.example.test:8080?")]
    [InlineData("http://proxy.example.test:8080?route=canary")]
    [InlineData("http://proxy.example.test:8080#")]
    [InlineData("http://proxy.example.test:8080#fragment")]
    [InlineData("http://proxy.example.test:0")]
    public void Validate_CustomHttpRejectsUnsafeOrAmbiguousUriShapes(string value)
    {
        var settings = Settings(WindowsProxyMode.CustomHttp, new Uri(value, UriKind.Absolute));

        var exception = Assert.Throws<ContractValidationException>(settings.Validate);

        Assert.Equal(ContractValidationError.InvalidValue, exception.Error);
        Assert.False(WindowsProxySettings.IsValidCustomHttpProxyUri(settings.CustomHttpProxyUri));
    }

    [Fact]
    public void Validate_CustomHttpRejectsOutOfRangePortBeforeItCanEnterSettings()
    {
        var created = Uri.TryCreate("http://proxy.example.test:65536", UriKind.Absolute, out var invalidPort);

        Assert.False(created);
        Assert.Null(invalidPort);
    }

    [Fact]
    public void Validate_RequiresUriOnlyForCustomHttpAndRejectsUndefinedModes()
    {
        var missingCustomUri = Settings(WindowsProxyMode.CustomHttp, null);
        var systemUri = Settings(WindowsProxyMode.SystemDefault, new Uri("http://proxy.example.test:8080"));
        var disabledUri = Settings(WindowsProxyMode.Disabled, new Uri("http://proxy.example.test:8080"));
        var undefinedMode = Settings((WindowsProxyMode)999, null);

        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(missingCustomUri.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(systemUri.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(disabledUri.Validate).Error);
        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(undefinedMode.Validate).Error);
    }

    [Fact]
    public void Validate_RejectsUnsupportedVersionsAndPrintableFormDoesNotExposeProxyUri()
    {
        var future = new WindowsProxySettings(
            WindowsProxySettingsMigration.CurrentVersion + 1,
            WindowsProxyMode.SystemDefault,
            null);
        var customUri = new Uri("http://proxy-canary.example.test:8080");
        var custom = Settings(WindowsProxyMode.CustomHttp, customUri);

        Assert.Equal(ContractValidationError.InvalidValue,
            Assert.Throws<ContractValidationException>(future.Validate).Error);
        Assert.DoesNotContain(customUri.AbsoluteUri, custom.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("proxy-canary", custom.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("credential", custom.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsProxySettings Settings(WindowsProxyMode mode, Uri? customUri) => new(
        WindowsProxySettingsMigration.CurrentVersion,
        mode,
        customUri);
}
