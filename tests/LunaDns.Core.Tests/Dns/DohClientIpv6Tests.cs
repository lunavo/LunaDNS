using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

/// <summary>Chỉ chạy khi máy có IPv6 loopback (một số runner CI không có).</summary>
public sealed class Ipv6LoopbackFactAttribute : FactAttribute
{
    public Ipv6LoopbackFactAttribute()
    {
        if (!Ipv6LoopbackAvailable()) Skip = "Máy không có IPv6 loopback.";
    }

    private static bool Ipv6LoopbackAvailable()
    {
        if (!Socket.OSSupportsIPv6) return false;
        try
        {
            using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}

public sealed class DohClientIpv6Tests
{
    [Ipv6LoopbackFact]
    public async Task Ipv6_literal_endpoint_connects_to_bootstrap_address()
    {
        using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var endpoint = new Uri($"https://[::1]:{port}/dns-query");
        // Host giữ ngoặc vuông, DnsSafeHost thì không. SocketsHttpHandler (.NET 8) đưa dạng có ngoặc cho callback;
        // callback khớp cả hai dạng, test đỏ nếu bỏ nhánh so Host.
        Assert.Equal("[::1]", endpoint.Host);
        Assert.Equal("::1", endpoint.DnsSafeHost);
        using var client = DohClient.Create([new DohServer("Tự nhập", endpoint, [IPAddress.IPv6Loopback])]);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var accept = listener.AcceptTcpClientAsync(timeout.Token).AsTask();
        // TLS sẽ hỏng vì listener không bắt tay; chỉ cần thấy kết nối tới đúng IP.
        var query = client.QueryAsync(TestDnsMessage.Query("store.steampowered.com"), timeout.Token);
        using var accepted = await accept;

        Assert.Equal(IPAddress.IPv6Loopback, ((IPEndPoint)accepted.Client.RemoteEndPoint!).Address);
        accepted.Close();
        await Assert.ThrowsAnyAsync<Exception>(() => query);
    }
}
