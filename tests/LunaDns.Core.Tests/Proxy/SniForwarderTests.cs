using System.Net;
using System.Net.Sockets;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Tls;

namespace LunaDns.Core.Tests.Proxy;

public sealed class SniForwarderTests : IAsyncLifetime
{
    private const string Host = "prod-live-front.playbattlegrounds.com";
    private static readonly byte[] Hello = TestClientHello.Build(Host);
    private static readonly IPEndPoint AnyLoopbackPort = new(IPAddress.Loopback, 0);

    private readonly FakeConnector _connector = new();
    private SniForwarder _forwarder = null!;

    public Task InitializeAsync()
    {
        _forwarder = Start(AnyLoopbackPort);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _forwarder.DisposeAsync();

    private SniForwarder Start(IPEndPoint endPoint) =>
        SniForwarder.Start(endPoint, new SniForwarderOptions { Domains = DomainCatalog.For(DomainGroup.Pubg) }, _connector);

    private async Task<NetworkStream> ConnectAsync()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        await socket.ConnectAsync(_forwarder.LocalEndPoint);
        return new NetworkStream(socket, ownsSocket: true);
    }

    private static async Task<bool> ClosedByServerAsync(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            return await stream.ReadAsync(new byte[1], timeout.Token) == 0;
        }
        catch (IOException)
        {
            // RST cũng là đóng.
            return true;
        }
    }

    [Fact]
    public async Task Forwards_split_hello_then_payload_for_listed_sni()
    {
        var upstream = new RecordingStream();
        string? connectedHost = null;
        var connectedPort = 0;
        _connector.Connect = (host, port) =>
        {
            connectedHost = host;
            connectedPort = port;
            return upstream;
        };

        await using var client = await ConnectAsync();
        await client.WriteAsync(Hello);
        await client.WriteAsync("ping"u8.ToArray());
        await Eventually.TrueAsync(() => upstream.TotalBytes == Hello.Length + 4);

        Assert.Equal(Host, connectedHost);
        Assert.Equal(443, connectedPort);
        Assert.Single(upstream.Writes[0]);
        Assert.Equal(Hello.Concat("ping"u8.ToArray()).ToArray(), upstream.Writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task Relays_both_directions()
    {
        await using var origin = new EchoOrigin();
        _connector.Connect = (_, _) => origin.Connect();

        await using var client = await ConnectAsync();
        await client.WriteAsync(Hello);
        var echoed = new byte[Hello.Length];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await client.ReadExactlyAsync(echoed, timeout.Token);

        Assert.Equal(Hello, echoed);
    }

    [Fact]
    public async Task Closes_when_sni_is_not_listed()
    {
        await using var client = await ConnectAsync();
        await client.WriteAsync(TestClientHello.Build("store.steampowered.com"));

        Assert.True(await ClosedByServerAsync(client));
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public async Task Closes_when_hello_has_no_sni()
    {
        await using var client = await ConnectAsync();
        await client.WriteAsync(TestClientHello.Build(null));

        Assert.True(await ClosedByServerAsync(client));
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public async Task Closes_on_non_tls_bytes()
    {
        await using var client = await ConnectAsync();
        await client.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n"));

        Assert.True(await ClosedByServerAsync(client));
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public void Second_bind_on_same_port_reports_port_in_use()
    {
        var ex = Assert.Throws<SplitProxyException>(() => Start(_forwarder.LocalEndPoint));

        Assert.True(ex.Error is ProxyStartError.PortInUse or ProxyStartError.AccessDenied, ex.Error.ToString());
    }

    [Fact]
    public async Task Upstream_failure_closes_connection_and_keeps_listening()
    {
        _connector.Connect = (_, _) => throw new IOException("máy chủ không trả lời");
        await using (var first = await ConnectAsync())
        {
            await first.WriteAsync(Hello);
            Assert.True(await ClosedByServerAsync(first));
        }

        var upstream = new RecordingStream();
        _connector.Connect = (_, _) => upstream;
        await using var second = await ConnectAsync();
        await second.WriteAsync(Hello);
        await Eventually.TrueAsync(() => upstream.TotalBytes == Hello.Length);
    }

    [Fact]
    public async Task Dispose_completes_and_is_idempotent()
    {
        await _forwarder.DisposeAsync();

        Assert.True(_forwarder.Completion.IsCompleted);
        await _forwarder.DisposeAsync();
    }
}
