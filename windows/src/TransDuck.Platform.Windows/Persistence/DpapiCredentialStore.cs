// Copyright (c) 2026 maywine. All rights reserved.

using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.Platform.Windows.Persistence;

/// <summary>
/// Stores versioned credential envelopes protected for the current Windows user by DPAPI.
/// </summary>
public sealed class DpapiCredentialStore : ICredentialStore, IDisposable
{
    private const byte EnvelopeVersion = 1;
    private static readonly byte[] Magic = "EDC1"u8.ToArray();
    private static readonly byte[] Entropy = "TransDuck.DpapiCredentialStore.v1"u8.ToArray();
    private readonly string _credentialsDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    /// <summary>Creates a store using the credential directory resolved by Windows data paths.</summary>
    public DpapiCredentialStore(WindowsDataPaths paths)
        : this((paths ?? throw new ArgumentNullException(nameof(paths))).CredentialsDirectoryPath)
    {
    }

    /// <summary>Creates a store using an injected credential directory without writing it.</summary>
    public DpapiCredentialStore(string credentialsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialsDirectory);
        _credentialsDirectory = Path.GetFullPath(credentialsDirectory);
    }

    /// <inheritdoc />
    public async Task<PersistenceReadResult<CredentialSecret>> GetAsync(
        CredentialKey key,
        CancellationToken cancellationToken)
    {
        if (!TryValidateKey(key))
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.InvalidData);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(status);
        }

        byte[]? envelope = null;
        byte[]? protectedBytes = null;
        byte[]? plaintext = null;
        try
        {
            var path = GetCredentialPath(key);
            if (!File.Exists(path))
            {
                return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.NotFound);
            }

            envelope = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            if (!TryReadEnvelope(envelope, out protectedBytes))
            {
                return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.CorruptData);
            }

            plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return PersistenceReadResult<CredentialSecret>.Success(CredentialSecret.FromUtf8(plaintext));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (CryptographicException)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.CorruptData);
        }
        catch (DecoderFallbackException)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.CorruptData);
        }
        catch (ArgumentException)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.CorruptData);
        }
        catch (IOException)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (envelope is not null)
            {
                CryptographicOperations.ZeroMemory(envelope);
            }

            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PersistenceResult> SetAsync(
        CredentialKey key,
        CredentialSecret secret,
        CancellationToken cancellationToken)
    {
        if (!TryValidateKey(key) || secret is null)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceResult.FromStatus(status);
        }

        byte[]? plaintext = null;
        byte[]? protectedBytes = null;
        byte[]? envelope = null;
        try
        {
            plaintext = secret.ExportUtf8();
            protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            envelope = CreateEnvelope(protectedBytes);
            await AtomicFileWriter.WriteBytesAsync(
                    GetCredentialPath(key),
                    envelope,
                    cancellationToken)
                .ConfigureAwait(false);
            return PersistenceResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (CryptographicException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (ObjectDisposedException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (IOException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }

            if (protectedBytes is not null)
            {
                CryptographicOperations.ZeroMemory(protectedBytes);
            }

            if (envelope is not null)
            {
                CryptographicOperations.ZeroMemory(envelope);
            }

            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PersistenceResult> RemoveAsync(CredentialKey key, CancellationToken cancellationToken)
    {
        if (!TryValidateKey(key))
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
        }

        var entryStatus = await TryEnterAsync(cancellationToken).ConfigureAwait(false);
        if (entryStatus is { } status)
        {
            return PersistenceResult.FromStatus(status);
        }

        try
        {
            var path = GetCredentialPath(key);
            if (!File.Exists(path))
            {
                return PersistenceResult.FromStatus(PersistenceStatus.NotFound);
            }

            File.Delete(path);
            return PersistenceResult.Success();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.Cancelled);
        }
        catch (IOException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        catch (UnauthorizedAccessException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }
    }

    private string GetCredentialPath(CredentialKey key)
    {
        var keyBytes = Encoding.UTF8.GetBytes(key.CanonicalValue);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(keyBytes)).ToLowerInvariant();
            return Path.Combine(_credentialsDirectory, hash + ".credential");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
        }
    }

    private static byte[] CreateEnvelope(ReadOnlySpan<byte> protectedBytes)
    {
        var envelope = new byte[Magic.Length + 1 + sizeof(int) + protectedBytes.Length];
        Magic.CopyTo(envelope, 0);
        envelope[Magic.Length] = EnvelopeVersion;
        BinaryPrimitives.WriteInt32LittleEndian(
            envelope.AsSpan(Magic.Length + 1, sizeof(int)),
            protectedBytes.Length);
        protectedBytes.CopyTo(envelope.AsSpan(Magic.Length + 1 + sizeof(int)));
        return envelope;
    }

    private static bool TryReadEnvelope(byte[] envelope, out byte[] protectedBytes)
    {
        protectedBytes = [];
        if (envelope.Length < Magic.Length + 1 + sizeof(int) ||
            !envelope.AsSpan(0, Magic.Length).SequenceEqual(Magic) ||
            envelope[Magic.Length] != EnvelopeVersion)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(
            envelope.AsSpan(Magic.Length + 1, sizeof(int)));
        var payloadOffset = Magic.Length + 1 + sizeof(int);
        if (payloadLength <= 0 || payloadLength != envelope.Length - payloadOffset)
        {
            return false;
        }

        protectedBytes = envelope.AsSpan(payloadOffset, payloadLength).ToArray();
        return true;
    }

    private async Task<PersistenceStatus?> TryEnterAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            return PersistenceStatus.IoFailure;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (Volatile.Read(ref _disposeRequested) != 0)
            {
                _gate.Release();
                return PersistenceStatus.IoFailure;
            }

            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PersistenceStatus.Cancelled;
        }
    }

    private static bool TryValidateKey(CredentialKey? key)
    {
        if (key is null)
        {
            return false;
        }

        try
        {
            key.Validate();
            return true;
        }
        catch (ContractValidationException)
        {
            return false;
        }
    }
}
