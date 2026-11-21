using System.Net;
using System.Net.Sockets;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Tls;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Proxy;

public sealed class SplitProxyTests : IAsyncLifetime
{
    private const string Host = "store.steampowered.com";
    private static readonly byte[] Hello = TestClientHello.Build(Host);

    private readonly FakeConnector _connector = new();
    private SplitProxy _proxy = null!;

    public Task InitializeAsync()
    {
        _proxy = Start();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _proxy.DisposeAsync();

    private SplitProxy Start(HelloSplit? split = null, int firstPort = 0, int lastPort = 0) =>
        SplitProxy.Start(
            new SplitProxyOptions { Domains = DomainCatalog.All, FirstPort = firstPort, LastPort = lastPort, Split = split ?? HelloSplit.Default },
            _connector);

    private RecordingStream UseRecordingUpstream()
    {
        var upstream = new RecordingStream();
        _connector.Connect = (_, _) => upstream;
        return upstream;
    }

    private static async Task<NetworkStream> OpenTunnelAsync(SplitProxy proxy, string authority = Host + ":443")
    {
        var stream = await ProxyTestClient.ConnectAsync(proxy.Port);
        await ProxyTestClient.SendAsync(stream, $"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n\r\n");
        var head = await ProxyTestClient.ReadHeadAsync(stream);
        Assert.StartsWith("HTTP/1.1 200 ", head, StringComparison.Ordinal);
        return stream;
    }

    private static Socket OccupyLoopbackPort()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Listen();
        return socket;
    }

    // ---- khởi động ----

    [Fact]
    public void Listens_on_loopback_and_exposes_pac_url()
    {
        Assert.Equal(IPAddress.Loopback, _proxy.LocalEndPoint.Address);
        Assert.Equal(_proxy.LocalEndPoint.Port, _proxy.Port);
        Assert.Equal(new Uri($"http://127.0.0.1:{_proxy.Port}/proxy.pac"), _proxy.PacUrl);
    }

    [Fact]
    public async Task Takes_next_port_when_first_is_busy()
    {
        // Nếu tình cờ cổng busy + 1 cũng đang có app khác giữ thì test này lỗi; chạy lại là qua.
        using var occupier = OccupyLoopbackPort();
        var busy = ((IPEndPoint)occupier.LocalEndPoint!).Port;

        await using var proxy = Start(firstPort: busy, lastPort: busy + 1);

        Assert.Equal(busy + 1, proxy.Port);
    }

    [Fact]
    public void Throws_PortInUse_when_whole_range_is_busy()
    {
        using var occupier = OccupyLoopbackPort();
        var busy = ((IPEndPoint)occupier.LocalEndPoint!).Port;

        var ex = Assert.Throws<SplitProxyException>(() => Start(firstPort: busy, lastPort: busy));

        Assert.Equal(ProxyStartError.PortInUse, ex.Error);
    }

    [Fact]
    public async Task Dispose_completes_the_accept_loop()
    {
        var proxy = Start();

        await proxy.DisposeAsync();
        await proxy.DisposeAsync(); // gọi hai lần không lỗi

        Assert.True(proxy.Completion.IsCompletedSuccessfully);
    }

    // ---- PAC và yêu cầu cục bộ ----

    [Fact]
    public async Task Serves_pac_built_for_its_own_port()
    {
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, "GET /proxy.pac HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", response, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/x-ns-proxy-autoconfig\r\n", response, StringComparison.Ordinal);
        Assert.EndsWith("\r\n\r\n" + PacBuilder.Build(_proxy.Port, DomainCatalog.All), response, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GET / HTTP/1.1", "404")]
    [InlineData("GET /proxy.pac?x=1 HTTP/1.1", "404")]
    [InlineData("POST /proxy.pac HTTP/1.1", "405")]
    [InlineData("GET http://store.steampowered.com/ HTTP/1.1", "403")]
    [InlineData("GET /proxy.pac HTTP/9", "400")]
    public async Task Answers_other_requests_with_status(string requestLine, string status)
    {
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, requestLine + "\r\n\r\n");

        Assert.StartsWith($"HTTP/1.1 {status} ", response, StringComparison.Ordinal);
        Assert.Contains("Connection: close\r\n", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_head_gets_431()
    {
        // Gửi đúng 8192 byte, không có dòng trống: proxy đã đọc hết nên trả lời không bị RST nuốt mất.
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, new string('a', ProxyRequestParser.MaxHeadLength));

        Assert.StartsWith("HTTP/1.1 431 ", response, StringComparison.Ordinal);
    }

    // ---- CONNECT bị từ chối ----

    [Theory]
    [InlineData("example.com:443")]
    [InlineData("evilsteampowered.com:443")]
    [InlineData("store.steampowered.com:80")]
    [InlineData("127.0.0.1:443")]
    public async Task Refuses_connect_outside_game_domains_or_port_443(string authority)
    {
        var response = await ProxyTestClient.RequestAsync(_proxy.Port, $"CONNECT {authority} HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 403 ", response, StringComparison.Ordinal);
        Assert.Equal(0, _connector.Calls);
    }

    [Fact]
    public async Task Upstream_failure_becomes_502()
    {
        _connector.Connect = (_, _) => throw new IOException("máy chủ không nối được");

        var response = await ProxyTestClient.RequestAsync(_proxy.Port, $"CONNECT {Host}:443 HTTP/1.1\r\n\r\n");

        Assert.StartsWith("HTTP/1.1 502 ", response, StringComparison.Ordinal);
    }

    // ---- cắt gói ----

    [Fact]
    public async Task Splits_client_hello_after_first_byte()
    {
        var upstream = UseRecordingUpstream();
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(Hello);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length);

        var writes = upstream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task MidSni_mode_cuts_inside_host_name()
    {
        await using var proxy = Start(new HelloSplit(SplitMode.MidSni));
        var upstream = UseRecordingUpstream();
        Assert.True(ClientHelloParser.TryFindSni(Hello, out var sni));
        await using var tunnel = await OpenTunnelAsync(proxy);

        await tunnel.WriteAsync(Hello);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length);

        Assert.Equal(sni.Offset + sni.Length / 2, upstream.Writes[0].Length);
    }

    [Fact]
    public async Task Waits_for_whole_record_before_splitting()
    {
        var upstream = UseRecordingUpstream();
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(Hello.AsMemory(0, 3));
        await Task.Delay(50);
        await tunnel.WriteAsync(Hello.AsMemory(3, 40));
        await Task.Delay(50);
        await tunnel.WriteAsync(Hello.AsMemory(43));
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length);

        var writes = upstream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello.Length - 1, writes[1].Length);
    }

    [Fact]
    public async Task Forwards_bytes_that_follow_the_record()
    {
        var upstream = UseRecordingUpstream();
        byte[] sent = [.. Hello, 1, 2, 3, 4];
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(sent);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == sent.Length);

        var writes = upstream.Writes;
        Assert.Single(writes[0]);
        Assert.Equal(Hello.Length - 1, writes[1].Length);
        Assert.Equal(sent, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task Forwards_non_tls_payload_unsplit()
    {
        var upstream = UseRecordingUpstream();
        var payload = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\n");
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(payload);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == payload.Length);

        Assert.Equal(payload, Assert.Single(upstream.Writes));
    }

    // ---- chuyển tiếp ----

    [Fact]
    public async Task Relays_both_directions()
    {
        await using var origin = new EchoOrigin();
        _connector.Connect = (_, _) => origin.Connect();
        byte[] sent = [.. Hello, .. "ping"u8];
        await using var tunnel = await OpenTunnelAsync(_proxy);

        await tunnel.WriteAsync(sent);

        Assert.Equal(sent, await ProxyTestClient.ReadExactAsync(tunnel, sent.Length));
    }

    [Fact]
    public async Task Counts_open_tunnels()
    {
        UseRecordingUpstream();
        var tunnel = await OpenTunnelAsync(_proxy);
        await ProxyTestClient.WaitUntilAsync(() => _proxy.ActiveConnections == 1);

        await tunnel.DisposeAsync();

        await ProxyTestClient.WaitUntilAsync(() => _proxy.ActiveConnections == 0);
    }

    [Fact]
    public async Task Records_mode_sends_two_records_upstream()
    {
        await using var proxy = Start(new HelloSplit(SplitMode.MidSni, Records: true));
        var upstream = UseRecordingUpstream();
        Assert.True(ClientHelloParser.TryFindSni(Hello, out var sni));
        await using var tunnel = await OpenTunnelAsync(proxy);

        await tunnel.WriteAsync(Hello);
        await ProxyTestClient.WaitUntilAsync(() => upstream.TotalBytes == Hello.Length + 5);

        var writes = upstream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(sni.Offset + sni.Length / 2, writes[0].Length);
        Assert.All(writes, w => Assert.Equal(0x16, w[0]));
    }
}
