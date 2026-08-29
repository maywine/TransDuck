// Copyright (c) 2026 maywine. All rights reserved.

using System.Text;
using System.Security.Cryptography;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Core.Persistence;

/// <summary>
/// Identifies a credential by a validated provider and optional configured instance.
/// </summary>
public sealed record CredentialKey(string ProviderId, string? InstanceId = null)
{
    /// <summary>Gets a stable non-secret representation used only for deterministic derivation.</summary>
    public string CanonicalValue => InstanceId is null
        ? ProviderId
        : ProviderId + ":" + InstanceId;

    /// <summary>Validates the stable provider and optional instance identifier.</summary>
    public void Validate() => new ProviderDescriptor(ProviderId, InstanceId).Validate();
}

/// <summary>
/// Holds credential text without allowing default diagnostic formatting to reveal it.
/// </summary>
public sealed class CredentialSecret : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] _utf8;
    private int _disposed;

    /// <summary>Creates a secret from non-empty credential text.</summary>
    public CredentialSecret(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        _utf8 = Utf8.GetBytes(value);
    }

    /// <summary>Creates a secret from non-empty UTF-8 data without formatting it for diagnostics.</summary>
    public static CredentialSecret FromUtf8(ReadOnlySpan<byte> value)
    {
        if (value.Length == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        _ = Utf8.GetString(value);
        return new CredentialSecret(value.ToArray());
    }

    /// <summary>Copies the credential bytes for an explicit encryption boundary.</summary>
    public byte[] ExportUtf8()
    {
        ThrowIfDisposed();
        return _utf8.ToArray();
    }

    /// <summary>Returns the credential text for an explicit authenticated request boundary.</summary>
    public string Reveal()
    {
        ThrowIfDisposed();
        return Utf8.GetString(_utf8);
    }

    /// <summary>Zeros the in-memory credential bytes and prevents further access.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            CryptographicOperations.ZeroMemory(_utf8);
        }
    }

    /// <inheritdoc />
    public override string ToString() => "CredentialSecret(***redacted***)";

    private CredentialSecret(byte[] utf8)
    {
        _utf8 = utf8;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
