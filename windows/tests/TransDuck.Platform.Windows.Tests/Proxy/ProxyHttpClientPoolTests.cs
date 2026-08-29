// Copyright (c) 2026 maywine. All rights reserved.

using System.Net.Http;
using System.Reflection;
using System.Threading;
using TransDuck.Platform.Windows.Proxy;

namespace TransDuck.Platform.Windows.Tests.Proxy;

public sealed class ProxyHttpClientPoolTests
{
    [Fact]
    public void Constructor_CreatesSystemDefaultAndDirectHandlersWithoutCredentialOptions()
    {
        using var factory = new TrackingHandlerFactory();
        using var pool = new ProxyHttpClientPool(factory);

        Assert.Equal(WindowsProxySettings.Default, pool.CurrentSettings);
        Assert.Equal(1, pool.CurrentGeneration);
        Assert.Equal(
            new[]
            {
                new ProxyHttpTransportOptions(WindowsProxyMode.SystemDefault, null),
                ProxyHttpTransportOptions.Direct,
            },
            factory.Options);
        Assert.Equal(
            new[] { "CustomHttpProxyUri", "Mode" },
            typeof(ProxyHttpTransportOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain(typeof(ProxyHttpTransportOptions).GetProperties(BindingFlags.Instance | BindingFlags.Public), property =>
            property.Name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("user", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("auth", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Constructor_CreatesExpectedRoutedAndDirectPoliciesForAllModes()
    {
        foreach (var settings in new[]
                 {
                     WindowsProxySettings.Default,
                     Custom("http://proxy.example.test:8080"),
                     Disabled(),
                 })
        {
            using var factory = new TrackingHandlerFactory();
            using var pool = new ProxyHttpClientPool(settings, factory);

            Assert.Equal(settings, pool.CurrentSettings);
            Assert.Equal(settings.Mode, factory.Options[0].Mode);
            Assert.Equal(settings.CustomHttpProxyUri, factory.Options[0].CustomHttpProxyUri);
            Assert.Equal(ProxyHttpTransportOptions.Direct, factory.Options[1]);
        }
    }

    [Fact]
    public void Update_PublishesNewGenerationAndDefersOldHandlerDisposalUntilOldLeaseCompletes()
    {
        using var factory = new TrackingHandlerFactory();
        using var pool = new ProxyHttpClientPool(Custom("http://proxy-one.example.test:8080"), factory);
        using var oldLease = pool.AcquireLease(new Uri("https://provider.example.test/translate"));
        var firstGenerationHandlers = factory.Handlers.Take(2).ToArray();

        var updatedGeneration = pool.Update(Custom("http://proxy-two.example.test:8081"));
        using var newLease = pool.AcquireLease(new Uri("https://provider.example.test/translate"));
        var secondGenerationHandlers = factory.Handlers.Skip(2).Take(2).ToArray();

        Assert.Equal(2, updatedGeneration);
        Assert.Equal(1, oldLease.Generation);
        Assert.Equal(2, newLease.Generation);
        Assert.False(oldLease.UsesDirectConnection);
        Assert.False(newLease.UsesDirectConnection);
        Assert.NotSame(oldLease.Client, newLease.Client);
        Assert.All(firstGenerationHandlers, handler => Assert.Equal(0, handler.DisposeCount));
        Assert.All(secondGenerationHandlers, handler => Assert.Equal(0, handler.DisposeCount));

        oldLease.Dispose();

        Assert.All(firstGenerationHandlers, handler => Assert.Equal(1, handler.DisposeCount));
        Assert.All(secondGenerationHandlers, handler => Assert.Equal(0, handler.DisposeCount));
    }

    [Fact]
    public void Dispose_WithAnActiveLeaseDefersHandlerDisposalUntilTheLeaseCompletes()
    {
        using var factory = new TrackingHandlerFactory();
        var pool = new ProxyHttpClientPool(Custom("http://proxy.example.test:8080"), factory);
        var lease = pool.AcquireLease(new Uri("https://provider.example.test/translate"));
        var handlers = factory.Handlers.ToArray();

        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _ = pool.CurrentSettings);
        Assert.Throws<ObjectDisposedException>(() => pool.AcquireLease(new Uri("https://provider.example.test/translate")));
        Assert.All(handlers, handler => Assert.Equal(0, handler.DisposeCount));

        lease.Dispose();

        Assert.All(handlers, handler => Assert.Equal(1, handler.DisposeCount));
    }

    [Fact]
    public void AcquireLease_ForcesAllLoopbackShapesDirectButKeepsPrivateNetworksRouted()
    {
        using var factory = new TrackingHandlerFactory();
        using var pool = new ProxyHttpClientPool(Custom("http://proxy.example.test:8080"), factory);
        var loopbackDestinations = new[]
        {
            "http://localhost:8080/",
            "http://localhost.:8080/",
            "http://api.localhost:8080/",
            "http://127.0.0.1:8080/",
            "http://127.255.255.254:8080/",
            "http://[::1]:8080/",
            "http://[::ffff:127.0.0.1]:8080/",
        };
        var privateDestinations = new[]
        {
            "http://10.0.0.1:8080/",
            "http://172.16.0.1:8080/",
            "http://192.168.1.1:8080/",
        };

        foreach (var destination in loopbackDestinations)
        {
            using var lease = pool.AcquireLease(new Uri(destination, UriKind.Absolute));
            Assert.True(lease.UsesDirectConnection, destination);
        }

        foreach (var destination in privateDestinations)
        {
            using var lease = pool.AcquireLease(new Uri(destination, UriKind.Absolute));
            Assert.False(lease.UsesDirectConnection, destination);
        }
    }

    private static WindowsProxySettings Custom(string value) => new(
        WindowsProxySettingsMigration.CurrentVersion,
        WindowsProxyMode.CustomHttp,
        new Uri(value, UriKind.Absolute));

    private static WindowsProxySettings Disabled() => new(
        WindowsProxySettingsMigration.CurrentVersion,
        WindowsProxyMode.Disabled,
        null);

    private sealed class TrackingHandlerFactory : IProxyHttpMessageHandlerFactory, IDisposable
    {
        public List<ProxyHttpTransportOptions> Options { get; } = [];

        public List<TrackingHandler> Handlers { get; } = [];

        public HttpMessageHandler Create(ProxyHttpTransportOptions options)
        {
            Options.Add(options);
            var handler = new TrackingHandler();
            Handlers.Add(handler);
            return handler;
        }

        public void Dispose()
        {
        }
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new InvalidOperationException("No proxy test sends a network request."));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Interlocked.Increment(ref _disposeCount);
            }

            base.Dispose(disposing);
        }
    }
}
