// Copyright (c) 2026 maywine. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using TransDuck.Core.Translation;

namespace TransDuck.Infrastructure.Translation;

/// <summary>
/// Packs the two Volcengine signing credentials into one versioned value for protected storage.
/// </summary>
public static class VolcengineCredentialCodec
{
    private const string Prefix = "volcengine:v1:";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Creates a versioned value containing an AccessKey ID and Secret Access Key.</summary>
    public static string Encode(string accessKeyId, string secretAccessKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey);

        var accessKeyBytes = Utf8.GetBytes(accessKeyId);
        var secretKeyBytes = Utf8.GetBytes(secretAccessKey);
        try
        {
            return Prefix + Convert.ToBase64String(accessKeyBytes) + ":" +
                Convert.ToBase64String(secretKeyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(accessKeyBytes);
            CryptographicOperations.ZeroMemory(secretKeyBytes);
        }
    }

    /// <summary>Decodes a stored value without exposing either credential through diagnostics.</summary>
    public static bool TryDecode(string? value, out TranslationCredentials credentials)
    {
        credentials = new TranslationCredentials(null);
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = value.IndexOf(':', Prefix.Length);
        if (separator <= Prefix.Length || separator == value.Length - 1 ||
            value.IndexOf(':', separator + 1) >= 0)
        {
            return false;
        }

        byte[]? accessKeyBytes = null;
        byte[]? secretKeyBytes = null;
        try
        {
            accessKeyBytes = Convert.FromBase64String(value[Prefix.Length..separator]);
            secretKeyBytes = Convert.FromBase64String(value[(separator + 1)..]);
            var accessKeyId = Utf8.GetString(accessKeyBytes);
            var secretAccessKey = Utf8.GetString(secretKeyBytes);
            if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(secretAccessKey))
            {
                return false;
            }

            credentials = new TranslationCredentials(accessKeyId, secretAccessKey);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        finally
        {
            if (accessKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(accessKeyBytes);
            }

            if (secretKeyBytes is not null)
            {
                CryptographicOperations.ZeroMemory(secretKeyBytes);
            }
        }
    }
}
