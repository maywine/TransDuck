// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Sockets;
using System.Text;
using TransDuck.Infrastructure.Proxy;

namespace TransDuck.Infrastructure.Tests.Proxy;

public sealed class ProxyHttpClientLoopbackTransportTests
{
    [Fact]
    public async Task CustomHttp_NonLoopbackDestinationReachesTheConfiguredLoopbackProxy()
    {
        using var proxy = new TcpListener(IPAddress.Loopback, port: 0);
        proxy.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var proxyUri = new Uri($"http://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}");
        using var pool = new ProxyHttpClientPool(Custom(proxyUri));
        var destination = new Uri("http://nonloopback.invalid:8080/proxy-path?x=1");
        using var lease = pool.AcquireLease(destination);

        var accepted = AcceptAndRespondAsync(proxy, "proxied", cancellation.Token);
        var response = await lease.Client.GetStringAsync(destination, cancellation.Token);
        var request = await accepted;

        Assert.False(lease.UsesDirectConnection);
        Assert.Equal("proxied", response);
        Assert.Equal("GET http://nonloopback.invalid:8080/proxy-path?x=1 HTTP/1.1", request.RequestLine);
        Assert.Contains(request.Headers, header =>
            string.Equals(header.Name, "Host", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(header.Value, "nonloopback.invalid:8080", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LoopbackDestinationBypassesCustomProxyAndReachesTheLoopbackDestinationDirectly()
    {
        using var destinationListener = new TcpListener(IPAddress.Loopback, port: 0);
        using var proxyListener = new TcpListener(IPAddress.Loopback, port: 0);
        destinationListener.Start();
        proxyListener.Start();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var proxyCancellation = new CancellationTokenSource();
        var proxyUri = new Uri($"http://127.0.0.1:{((IPEndPoint)proxyListener.LocalEndpoint).Port}");
        var destination = new Uri($"http://127.0.0.1:{((IPEndPoint)destinationListener.LocalEndpoint).Port}/direct-path?x=1");
        using var pool = new ProxyHttpClientPool(Custom(proxyUri));
        using var lease = pool.AcquireLease(destination);
        var directRequest = AcceptAndRespondAsync(destinationListener, "direct", cancellation.Token);
        var unexpectedProxyConnection = proxyListener.AcceptTcpClientAsync(proxyCancellation.Token).AsTask();

        try
        {
            var response = await lease.Client.GetStringAsync(destination, cancellation.Token);
            var request = await directRequest;

            Assert.True(lease.UsesDirectConnection);
            Assert.Equal("direct", response);
            Assert.Equal("GET /direct-path?x=1 HTTP/1.1", request.RequestLine);
            Assert.False(unexpectedProxyConnection.IsCompleted);
        }
        finally
        {
            await CancelAcceptAsync(unexpectedProxyConnection, proxyCancellation);
        }
    }

    private static ProxySettings Custom(Uri proxyUri) => new(
        ProxySettingsMigration.CurrentVersion,
        ProxyMode.CustomHttp,
        proxyUri);

    private static async Task<CapturedRequest> AcceptAndRespondAsync(
        TcpListener listener,
        string responseBody,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);
        var requestLine = await reader.ReadLineAsync().WaitAsync(cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(requestLine));
        var headers = new List<(string Name, string Value)>();
        while (true)
        {
            var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
            if (string.IsNullOrEmpty(line))
            {
                break;
            }

            var separator = line.IndexOf(':');
            Assert.True(separator > 0, "The loopback request header must be well formed.");
            headers.Add((line[..separator], line[(separator + 1)..].TrimStart()));
        }

        var response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " +
            Encoding.UTF8.GetByteCount(responseBody) + "\r\nConnection: close\r\n\r\n" + responseBody;
        var bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        return new CapturedRequest(requestLine!, headers);
    }

    private static async Task CancelAcceptAsync(Task<TcpClient> accept, CancellationTokenSource cancellation)
    {
        if (accept.IsCompletedSuccessfully)
        {
            (await accept).Dispose();
            return;
        }

        cancellation.Cancel();
        try
        {
            _ = await accept;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record CapturedRequest(string RequestLine, IReadOnlyList<(string Name, string Value)> Headers);
}
