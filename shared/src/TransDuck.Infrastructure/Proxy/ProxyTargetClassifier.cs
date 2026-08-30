// Copyright (c) 2026 maywine. All rights reserved.

using System.Net;
using System.Net.Sockets;

namespace TransDuck.Infrastructure.Proxy;

/// <summary>
/// Identifies the destination names that must stay direct regardless of proxy configuration.
/// </summary>
internal static class ProxyTargetClassifier
{
    public static bool RequiresDirectConnection(Uri destination)
    {
        var host = destination.DnsSafeHost;
        if (IsLocalhostName(host))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IsLoopback(address);
    }

    private static bool IsLocalhostName(string host)
    {
        var normalizedHost = host.TrimEnd('.');
        return string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
            normalizedHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopback(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return address.GetAddressBytes()[0] == 127;
        }

        if (address.Equals(IPAddress.IPv6Loopback))
        {
            return true;
        }

        return address.IsIPv4MappedToIPv6 && IsLoopback(address.MapToIPv4());
    }
}
