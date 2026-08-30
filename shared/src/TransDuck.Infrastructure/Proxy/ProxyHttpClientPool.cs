// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Http;
using TransDuck.Core.Contracts.V1;

namespace TransDuck.Infrastructure.Proxy;

/// <summary>
/// Describes the immutable transport policy used to construct one HTTP message handler.
/// </summary>
public sealed record ProxyHttpTransportOptions(ProxyMode Mode, Uri? CustomHttpProxyUri)
{
    /// <summary>Gets the transport policy that directly connects without a proxy.</summary>
    public static ProxyHttpTransportOptions Direct { get; } = new(ProxyMode.Disabled, null);

    /// <inheritdoc />
    public override string ToString() =>
        $"ProxyHttpTransportOptions(Mode={Mode}, HasCustomHttpProxy={CustomHttpProxyUri is not null})";
}

/// <summary>
/// Creates HTTP message handlers for immutable proxy transport generations.
/// </summary>
public interface IProxyHttpMessageHandlerFactory
{
    /// <summary>Creates a handler for the supplied immutable transport policy.</summary>
    HttpMessageHandler Create(ProxyHttpTransportOptions options);
}

/// <summary>
/// Owns HTTP clients grouped by immutable proxy configuration generations.
/// </summary>
public sealed class ProxyHttpClientPool : IDisposable
{
    private readonly object _gate = new();
    private readonly IProxyHttpMessageHandlerFactory _handlerFactory;
    private Generation? _currentGeneration;
    private int _disposeRequested;

    /// <summary>Creates a pool using the default SystemDefault proxy policy.</summary>
    public ProxyHttpClientPool()
        : this(ProxySettings.Default, handlerFactory: null)
    {
    }

    /// <summary>Creates a pool using an injected handler factory and the default proxy policy.</summary>
    public ProxyHttpClientPool(IProxyHttpMessageHandlerFactory handlerFactory)
        : this(
            ProxySettings.Default,
            handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory)))
    {
    }

    /// <summary>Creates a pool using the supplied initial proxy policy and handler factory.</summary>
    public ProxyHttpClientPool(
        ProxySettings initialSettings,
        IProxyHttpMessageHandlerFactory? handlerFactory = null)
    {
        ValidateSettings(initialSettings, nameof(initialSettings));
        _handlerFactory = handlerFactory ?? new DefaultProxyHttpMessageHandlerFactory();
        _currentGeneration = Generation.Create(1, initialSettings, _handlerFactory);
    }

    /// <summary>Gets the current immutable proxy settings snapshot.</summary>
    public ProxySettings CurrentSettings
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return GetCurrentGeneration().Settings;
            }
        }
    }

    /// <summary>Gets the generation assigned to newly acquired leases.</summary>
    public long CurrentGeneration
    {
        get
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                return GetCurrentGeneration().Number;
            }
        }
    }

    /// <summary>
    /// Publishes validated settings as a new generation so later leases cannot use the prior handler.
    /// </summary>
    public long Update(ProxySettings settings)
    {
        ValidateSettings(settings, nameof(settings));

        Generation? retiredGeneration = null;
        long generationNumber;
        lock (_gate)
        {
            ThrowIfDisposed();
            var current = GetCurrentGeneration();
            generationNumber = checked(current.Number + 1);
            var replacement = Generation.Create(generationNumber, settings, _handlerFactory);
            _currentGeneration = replacement;
            if (current.Retire())
            {
                retiredGeneration = current;
            }
        }

        retiredGeneration?.Dispose();
        return generationNumber;
    }

    /// <summary>
    /// Acquires the current transport generation for a destination until the returned lease is disposed.
    /// Keep the lease for the complete request and response-content lifetime.
    /// </summary>
    public ProxyHttpClientLease AcquireLease(Uri destination)
    {
        ValidateDestination(destination);

        lock (_gate)
        {
            ThrowIfDisposed();
            var generation = GetCurrentGeneration();
            var useDirectConnection = ProxyTargetClassifier.RequiresDirectConnection(destination);
            generation.AddLease();
            return new ProxyHttpClientLease(this, generation, useDirectConnection);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Generation? retiredGeneration = null;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
            {
                return;
            }

            if (_currentGeneration is { } current)
            {
                _currentGeneration = null;
                if (current.Retire())
                {
                    retiredGeneration = current;
                }
            }
        }

        retiredGeneration?.Dispose();
    }

    private void ReleaseLease(Generation generation)
    {
        var shouldDispose = false;
        lock (_gate)
        {
            shouldDispose = generation.ReleaseLease();
        }

        if (shouldDispose)
        {
            generation.Dispose();
        }
    }

    private Generation GetCurrentGeneration() => _currentGeneration ?? throw new ObjectDisposedException(
        nameof(ProxyHttpClientPool));

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeRequested) != 0)
        {
            throw new ObjectDisposedException(nameof(ProxyHttpClientPool));
        }
    }

    private static void ValidateSettings(ProxySettings? settings, string parameterName)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        try
        {
            settings.Validate();
        }
        catch (ContractValidationException exception)
        {
            throw new ArgumentException("Windows proxy settings are invalid.", parameterName, exception);
        }
    }

    private static void ValidateDestination(Uri? destination)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (!destination.IsAbsoluteUri || string.IsNullOrWhiteSpace(destination.Host))
        {
            throw new ArgumentException(
                "Proxy transport destination must be an absolute URI with a host.",
                nameof(destination));
        }
    }

    internal sealed class Generation : IDisposable
    {
        private readonly HttpMessageHandler[] _handlers;
        private int _leaseCount;
        private bool _retired;
        private int _disposeRequested;

        private Generation(
            long number,
            ProxySettings settings,
            HttpClient routedClient,
            HttpClient directClient,
            HttpMessageHandler[] handlers)
        {
            Number = number;
            Settings = settings;
            RoutedClient = routedClient;
            DirectClient = directClient;
            _handlers = handlers;
        }

        public long Number { get; }

        public ProxySettings Settings { get; }

        public HttpClient RoutedClient { get; }

        public HttpClient DirectClient { get; }

        public static Generation Create(
            long number,
            ProxySettings settings,
            IProxyHttpMessageHandlerFactory handlerFactory)
        {
            HttpMessageHandler? routedHandler = null;
            HttpMessageHandler? directHandler = null;
            HttpClient? routedClient = null;
            HttpClient? directClient = null;
            try
            {
                routedHandler = handlerFactory.Create(new ProxyHttpTransportOptions(
                    settings.Mode,
                    settings.CustomHttpProxyUri)) ?? throw new InvalidOperationException(
                    "Proxy HTTP handler factory returned no routed handler.");
                directHandler = handlerFactory.Create(ProxyHttpTransportOptions.Direct) ??
                    throw new InvalidOperationException(
                        "Proxy HTTP handler factory returned no direct handler.");
                // Bing can establish a session cookie while fetching its token before posting a translation.
                routedClient = CreateClient(routedHandler);
                directClient = CreateClient(directHandler);
                return new Generation(
                    number,
                    settings,
                    routedClient,
                    directClient,
                    DistinctHandlers(routedHandler, directHandler));
            }
            catch
            {
                routedClient?.Dispose();
                directClient?.Dispose();
                DisposeDistinctHandlers(routedHandler, directHandler);
                throw;
            }
        }

        public void AddLease()
        {
            checked
            {
                _leaseCount++;
            }
        }

        public bool ReleaseLease()
        {
            if (_leaseCount <= 0)
            {
                throw new InvalidOperationException("Proxy HTTP generation lease count underflow.");
            }

            _leaseCount--;
            return _retired && _leaseCount == 0;
        }

        public bool Retire()
        {
            _retired = true;
            return _leaseCount == 0;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
            {
                return;
            }

            RoutedClient.Dispose();
            DirectClient.Dispose();
            foreach (var handler in _handlers)
            {
                handler.Dispose();
            }
        }

        private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler, disposeHandler: false)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        private static HttpMessageHandler[] DistinctHandlers(
            HttpMessageHandler routedHandler,
            HttpMessageHandler directHandler) => ReferenceEquals(routedHandler, directHandler)
            ? [routedHandler]
            : [routedHandler, directHandler];

        private static void DisposeDistinctHandlers(
            HttpMessageHandler? routedHandler,
            HttpMessageHandler? directHandler)
        {
            routedHandler?.Dispose();
            if (directHandler is not null && !ReferenceEquals(routedHandler, directHandler))
            {
                directHandler.Dispose();
            }
        }
    }

    private sealed class DefaultProxyHttpMessageHandlerFactory : IProxyHttpMessageHandlerFactory
    {
        public HttpMessageHandler Create(ProxyHttpTransportOptions options)
        {
            return options.Mode switch
            {
                ProxyMode.SystemDefault => CreateSystemDefaultHandler(),
                ProxyMode.CustomHttp => CreateCustomHttpHandler(options.CustomHttpProxyUri),
                ProxyMode.Disabled => CreateDirectHandler(),
                _ => throw new ArgumentOutOfRangeException(nameof(options)),
            };
        }

        private static HttpClientHandler CreateSystemDefaultHandler() => new()
        {
            UseProxy = true,
            UseDefaultCredentials = false,
            DefaultProxyCredentials = null,
        };

        private static HttpClientHandler CreateCustomHttpHandler(Uri? proxyUri)
        {
            if (!ProxySettings.IsValidCustomHttpProxyUri(proxyUri))
            {
                throw new ArgumentException("Custom HTTP proxy settings are invalid.", nameof(proxyUri));
            }

            // Proxy authentication is intentionally unsupported even when a machine default exists.
            var proxy = new WebProxy(proxyUri!)
            {
                UseDefaultCredentials = false,
                Credentials = null,
                BypassProxyOnLocal = false,
                BypassList = Array.Empty<string>(),
            };
            return new HttpClientHandler
            {
                UseProxy = true,
                Proxy = proxy,
                UseDefaultCredentials = false,
                DefaultProxyCredentials = null,
            };
        }

        private static HttpClientHandler CreateDirectHandler() => new()
        {
            UseProxy = false,
            UseDefaultCredentials = false,
            DefaultProxyCredentials = null,
        };
    }

    /// <summary>
    /// Keeps one generation alive until its caller has completed all work using the selected client.
    /// </summary>
    public sealed class ProxyHttpClientLease : IDisposable
    {
        private readonly ProxyHttpClientPool _owner;
        private readonly Generation _generation;
        private int _disposeRequested;

        internal ProxyHttpClientLease(
            ProxyHttpClientPool owner,
            Generation generation,
            bool usesDirectConnection)
        {
            _owner = owner;
            _generation = generation;
            UsesDirectConnection = usesDirectConnection;
        }

        /// <summary>
        /// Gets the pool-owned client selected from this immutable generation; callers must not dispose it.
        /// </summary>
        public HttpClient Client => UsesDirectConnection
            ? _generation.DirectClient
            : _generation.RoutedClient;

        /// <summary>Gets the immutable generation number used by this lease.</summary>
        public long Generation => _generation.Number;

        /// <summary>Gets whether this destination is forced to bypass every proxy policy.</summary>
        public bool UsesDirectConnection { get; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeRequested, 1) == 0)
            {
                _owner.ReleaseLease(_generation);
            }
        }
    }
}
