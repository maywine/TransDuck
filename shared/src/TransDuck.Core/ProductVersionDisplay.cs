// Copyright (c) 2026 maywine. All rights reserved.

using System.Reflection;

namespace TransDuck.Core;

/// <summary>
/// Produces the stable product-version text shown to users.
/// </summary>
public static class ProductVersionDisplay
{
    private const string StableFallback = "v0.0.0";

    public static string FromAssembly(Assembly? assembly)
    {
        if (assembly is null)
        {
            return StableFallback;
        }

        if (TryGetInformationalVersion(assembly, out var informationalVersion) &&
            TryNormalizeInformationalVersion(informationalVersion, out var normalizedVersion))
        {
            return "v" + normalizedVersion;
        }

        return FormatAssemblyVersion(assembly);
    }

    private static bool TryGetInformationalVersion(
        Assembly assembly,
        out string? informationalVersion)
    {
        try
        {
            informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            return !string.IsNullOrWhiteSpace(informationalVersion);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            informationalVersion = null;
            return false;
        }
    }

    private static bool TryNormalizeInformationalVersion(
        string? informationalVersion,
        out string normalizedVersion)
    {
        normalizedVersion = string.Empty;
        var candidate = informationalVersion?.Trim();
        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var buildMetadata = candidate.IndexOf('+');
        if (buildMetadata >= 0)
        {
            var metadata = candidate[(buildMetadata + 1)..];
            if (!IsValidIdentifierList(metadata))
            {
                return false;
            }

            candidate = candidate[..buildMetadata];
        }

        if (candidate.StartsWith('v') || candidate.StartsWith('V'))
        {
            candidate = candidate[1..];
        }

        var prereleaseSeparator = candidate.IndexOf('-');
        var coreVersion = prereleaseSeparator < 0 ? candidate : candidate[..prereleaseSeparator];
        var prerelease = prereleaseSeparator < 0 ? null : candidate[(prereleaseSeparator + 1)..];
        if (!TryParseThreePartVersion(coreVersion) ||
            (prerelease is not null && !IsValidIdentifierList(prerelease)))
        {
            return false;
        }

        normalizedVersion = candidate;
        return true;
    }

    private static bool TryParseThreePartVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Build < 0 || version.Revision >= 0)
        {
            return false;
        }

        return version.Major >= 0 && version.Minor >= 0;
    }

    private static bool IsValidIdentifierList(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0 || identifier.Any(character =>
                    !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatAssemblyVersion(Assembly assembly)
    {
        try
        {
            var version = assembly.GetName().Version;
            if (version is not null && version.Major >= 0 && version.Minor >= 0)
            {
                var patch = version.Build >= 0 ? version.Build : 0;
                return $"v{version.Major}.{version.Minor}.{patch}";
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            // Version display must remain available even when reflection metadata is unavailable.
        }

        return StableFallback;
    }
}
