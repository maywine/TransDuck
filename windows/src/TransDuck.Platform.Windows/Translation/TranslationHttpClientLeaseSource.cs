// Copyright (c) 2026 maywine. All rights reserved.

using System.Net.Http;
using TransDuck.Platform.Windows.Proxy;

namespace TransDuck.Platform.Windows.Translation;

/// <summary>
/// Selects a client for one destination and keeps its transport generation alive while a provider enumerates.
/// </summary>
public interface ITranslationHttpClientLeaseSource
{
    /// <summary>Acquires a lease for the complete request, response, and stream lifetime.</summary>
    ITranslationHttpClientLease AcquireLease(Uri endpoint);
}

/// <summary>
/// Represents an owned client selection whose disposal ends the provider's transport-generation lease.
/// </summary>
public interface ITranslationHttpClientLease : IDisposable
{
    /// <summary>Gets the selected client. Callers must not dispose it.</summary>
    HttpClient Client { get; }
}

/// <summary>
/// Adapts proxy generations to the provider transport boundary without exposing proxy policy to adapters.
/// </summary>
public sealed class ProxyTranslationHttpClientLeaseSource : ITranslationHttpClientLeaseSource
{
    private readonly ProxyHttpClientPool _pool;

    /// <summary>Creates a lease source backed by a shared application proxy pool.</summary>
    public ProxyTranslationHttpClientLeaseSource(ProxyHttpClientPool pool)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
    }

    /// <inheritdoc />
    public ITranslationHttpClientLease AcquireLease(Uri endpoint) =>
        new ProxyTranslationHttpClientLease(_pool.AcquireLease(endpoint));

    private sealed class ProxyTranslationHttpClientLease : ITranslationHttpClientLease
    {
        private readonly ProxyHttpClientPool.ProxyHttpClientLease _lease;

        public ProxyTranslationHttpClientLease(ProxyHttpClientPool.ProxyHttpClientLease lease)
        {
            _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        }

        public HttpClient Client => _lease.Client;

        public void Dispose() => _lease.Dispose();
    }
}

internal sealed class FixedTranslationHttpClientLeaseSource : ITranslationHttpClientLeaseSource
{
    private readonly HttpClient _httpClient;

    public FixedTranslationHttpClientLeaseSource(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public ITranslationHttpClientLease AcquireLease(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return new FixedTranslationHttpClientLease(_httpClient);
    }

    private sealed class FixedTranslationHttpClientLease : ITranslationHttpClientLease
    {
        public FixedTranslationHttpClientLease(HttpClient httpClient)
        {
            Client = httpClient;
        }

        public HttpClient Client { get; }

        public void Dispose()
        {
        }
    }
}

internal static class TranslationHttpClientLeases
{
    public static ITranslationHttpClientLease? TryAcquire(
        ITranslationHttpClientLeaseSource source,
        Uri endpoint)
    {
        try
        {
            return source.AcquireLease(endpoint);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            return null;
        }
    }
}
