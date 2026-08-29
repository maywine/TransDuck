// Copyright (c) 2026 maywine. All rights reserved.

using System.Text.Json.Serialization;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Platform.Windows.Proxy;

/// <summary>
/// Defines the only persisted Windows proxy settings version accepted by this client.
/// </summary>
public static class WindowsProxySettingsMigration
{
    /// <summary>Gets the only Windows proxy settings Version accepted by this implementation.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// Selects the proxy policy used by Windows translation-provider requests.
/// </summary>
public enum WindowsProxyMode
{
    SystemDefault,
    CustomHttp,
    Disabled,
}

/// <summary>
/// Stores the non-secret, Windows-only proxy policy used by translation-provider requests.
/// </summary>
public sealed record WindowsProxySettings(
    [property: JsonRequired] int Version,
    [property: JsonRequired] WindowsProxyMode Mode,
    Uri? CustomHttpProxyUri)
{
    /// <summary>Gets the proxy policy used when no persisted settings document exists.</summary>
    public static WindowsProxySettings Default { get; } = new(
        WindowsProxySettingsMigration.CurrentVersion,
        WindowsProxyMode.SystemDefault,
        null);

    /// <summary>
    /// Validates the supported settings version and the closed custom HTTP proxy shape.
    /// </summary>
    public void Validate()
    {
        if (Version != WindowsProxySettingsMigration.CurrentVersion)
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Windows proxy settings version is not supported.");
        }

        if (!Enum.IsDefined(Mode))
        {
            throw new ContractValidationException(
                ContractValidationError.InvalidValue,
                "Windows proxy mode is not supported.");
        }

        switch (Mode)
        {
            case WindowsProxyMode.CustomHttp:
                if (!IsValidCustomHttpProxyUri(CustomHttpProxyUri))
                {
                    throw new ContractValidationException(
                        ContractValidationError.InvalidValue,
                        "Custom HTTP proxy settings are invalid.");
                }

                break;
            case WindowsProxyMode.SystemDefault:
            case WindowsProxyMode.Disabled:
                if (CustomHttpProxyUri is not null)
                {
                    throw new ContractValidationException(
                        ContractValidationError.InvalidValue,
                        "Only CustomHttp mode can include a proxy URI.");
                }

                break;
        }
    }

    /// <summary>
    /// Returns whether a URI is a credential-free absolute HTTP proxy endpoint with an explicit port.
    /// </summary>
    public static bool IsValidCustomHttpProxyUri(Uri? proxyUri)
    {
        if (proxyUri is null || !proxyUri.IsAbsoluteUri ||
            !string.Equals(proxyUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(proxyUri.Host) || proxyUri.Port is < 1 or > 65535 ||
            !string.IsNullOrEmpty(proxyUri.UserInfo) ||
            !string.IsNullOrEmpty(proxyUri.Query) || !string.IsNullOrEmpty(proxyUri.Fragment))
        {
            return false;
        }

        return TryGetAuthority(proxyUri, out var authority, out var suffix) &&
            !authority.Contains('@') && HasExplicitPort(authority) &&
            (suffix is "" or "/") &&
            proxyUri.HostNameType != UriHostNameType.Unknown;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"WindowsProxySettings(Version={Version}, Mode={Mode}, " +
        $"HasCustomHttpProxy={CustomHttpProxyUri is not null})";

    private static bool TryGetAuthority(Uri proxyUri, out string authority, out string suffix)
    {
        var original = proxyUri.OriginalString;
        var schemeEnd = original.IndexOf(':');
        if (schemeEnd < 0 || original.Length < schemeEnd + 3 ||
            original[schemeEnd + 1] != '/' || original[schemeEnd + 2] != '/')
        {
            authority = string.Empty;
            suffix = string.Empty;
            return false;
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = original.Length;
        foreach (var separator in new[] { '/', '?', '#' })
        {
            var separatorIndex = original.IndexOf(separator, authorityStart);
            if (separatorIndex >= 0 && separatorIndex < authorityEnd)
            {
                authorityEnd = separatorIndex;
            }
        }

        authority = original[authorityStart..authorityEnd];
        suffix = original[authorityEnd..];
        return authority.Length > 0;
    }

    private static bool HasExplicitPort(string authority)
    {
        var portSeparator = authority[0] == '['
            ? authority.IndexOf(']') + 1
            : authority.LastIndexOf(':');
        if (portSeparator <= 0 || portSeparator >= authority.Length - 1 ||
            authority[portSeparator] != ':')
        {
            return false;
        }

        return int.TryParse(
            authority[(portSeparator + 1)..],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var port) && port is >= 1 and <= 65535;
    }
}
