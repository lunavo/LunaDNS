using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Dns;

namespace LunaDns.Core.Tests.Proxy;

public sealed class TcpUpstreamConnectorTests : IDisposable
{
    private const string Host = "store.steampowered.com";
    private static readonly byte[] Ping = [42];

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly FakeResolver _resolver = new();
    private readonly int _port;

    public TcpUpstreamConnectorTests()
    {
        _listener.Start();
        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public void Dispose() => _listener.Stop();

    [Fact]
    public async Task Connects_to_resolved_address_with_nagle_off()
    {
        var connector = new TcpUpstreamConnector(_resolver);

        await using var stream = await connector.ConnectAsync(Host, _port, CancellationToken.None);
        using var accepted = await _listener.AcceptTcpClientAsync();
        await stream.WriteAsync(Ping);
        var got = new byte[1];
        await accepted.GetStream().ReadExactlyAsync(got);

        Assert.Equal(Ping, got);
        Assert.True(Assert.IsType<NetworkStream>(stream).Socket.NoDelay);
        Assert.Equal(Host, Assert.Single(_resolver.Hosts));
    }

    [Fact]
    public async Task Falls_back_to_next_address()
    {
        // 127.0.0.2 không có ai nghe (listener chỉ bind 127.0.0.1); Windows mất khoảng 2 giây mới báo từ chối.
        _resolver.Respond = _ => [IPAddress.Parse("127.0.0.2"), IPAddress.Loopback];

        await using var stream = await new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None);

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task Throws_IOException_when_name_has_no_address()
    {
        _resolver.Respond = _ => [];

        await Assert.ThrowsAsync<IOException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));
    }

    [Fact]
    public async Task Throws_IOException_when_nothing_listens()
    {
        _listener.Stop();

        var ex = await Assert.ThrowsAsync<IOException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));

        Assert.IsType<SocketException>(ex.InnerException);
    }

    [Fact]
    public async Task Lets_resolver_failure_through()
    {
        _resolver.Respond = _ => throw new DohException("DoH chết");

        await Assert.ThrowsAsync<DohException>(() => new TcpUpstreamConnector(_resolver).ConnectAsync(Host, _port, CancellationToken.None));
    }
}
