using System.Security.Cryptography;
using System.Text;
using TransDuck.Core.Contracts.V1;
using TransDuck.Core.Persistence;

namespace TransDuck.Platform.MacOS.Persistence;

public enum MacKeychainBackendStatus
{
    Succeeded,
    NotFound,
    Denied,
    Failed,
}

public sealed record MacKeychainReadResult(MacKeychainBackendStatus Status, byte[]? Value = null);

public interface IMacKeychainBackend : IDisposable
{
    MacKeychainReadResult Get(string service, string account);

    MacKeychainBackendStatus Set(string service, string account, ReadOnlySpan<byte> value);

    MacKeychainBackendStatus Remove(string service, string account);
}

/// <summary>
/// Stores provider credentials as generic-password items in the current user's macOS Keychain.
/// </summary>
public sealed class MacKeychainCredentialStore : ICredentialStore, IDisposable
{
    public const string ServiceName = "com.transduck.app.credentials";

    private readonly IMacKeychainBackend _backend;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _disposeRequested;

    public MacKeychainCredentialStore()
        : this(new SecurityFrameworkKeychainBackend())
    {
    }

    public MacKeychainCredentialStore(IMacKeychainBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

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

        byte[]? value = null;
        try
        {
            var result = await Task.Run(
                () => _backend.Get(ServiceName, key.CanonicalValue),
                CancellationToken.None).ConfigureAwait(false);
            value = result.Value;
            if (result.Status != MacKeychainBackendStatus.Succeeded || value is null)
            {
                return PersistenceReadResult<CredentialSecret>.FromStatus(Map(result.Status));
            }

            try
            {
                return PersistenceReadResult<CredentialSecret>.Success(CredentialSecret.FromUtf8(value));
            }
            catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException)
            {
                return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.CorruptData);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return PersistenceReadResult<CredentialSecret>.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            if (value is not null)
            {
                CryptographicOperations.ZeroMemory(value);
            }

            _gate.Release();
        }
    }

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

        byte[]? value = null;
        try
        {
            value = secret.ExportUtf8();
            var backendStatus = await Task.Run(
                () => _backend.Set(ServiceName, key.CanonicalValue, value),
                CancellationToken.None).ConfigureAwait(false);
            return PersistenceResult.FromStatus(Map(backendStatus));
        }
        catch (ObjectDisposedException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.InvalidData);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            if (value is not null)
            {
                CryptographicOperations.ZeroMemory(value);
            }

            _gate.Release();
        }
    }

    public async Task<PersistenceResult> RemoveAsync(
        CredentialKey key,
        CancellationToken cancellationToken)
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
            var backendStatus = await Task.Run(
                () => _backend.Remove(ServiceName, key.CanonicalValue),
                CancellationToken.None).ConfigureAwait(false);
            return PersistenceResult.FromStatus(Map(backendStatus));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return PersistenceResult.FromStatus(PersistenceStatus.IoFailure);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0)
        {
            _backend.Dispose();
        }
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

    private static PersistenceStatus Map(MacKeychainBackendStatus status) => status switch
    {
        MacKeychainBackendStatus.Succeeded => PersistenceStatus.Succeeded,
        MacKeychainBackendStatus.NotFound => PersistenceStatus.NotFound,
        MacKeychainBackendStatus.Denied => PersistenceStatus.IoFailure,
        _ => PersistenceStatus.IoFailure,
    };
}
