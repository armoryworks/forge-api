using System.Net;
using System.Net.Sockets;

namespace Forge.Api.Features.Webhooks;

public static class WebhookTargetGuard
{
    public static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        if (address.Equals(IPAddress.Broadcast)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254) || bytes[0] >= 224;
        }

        return address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal;
    }

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, ct);

        var allowed = addresses.Where(a => !IsBlocked(a)).ToArray();
        if (allowed.Length == 0)
            throw new HttpRequestException($"Webhook target {host} resolves only to loopback, link-local or reserved addresses.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
