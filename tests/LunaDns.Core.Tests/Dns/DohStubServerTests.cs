using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Dns;

public sealed class DohStubServerTests : IAsyncLifetime
{
    private const string Name = "store.steampowered.com";
    private static readonly byte[] Junk = [1, 2, 3];

    private readonly FakeUpstream _upstream = new();
    private DohStubServer _server = null!;

    public Task InitializeAsync()
    {
        _server = StartOn(0, _upstream);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _server.DisposeAsync();

    private static DohStubServer StartOn(int port, IDnsUpstream upstream) =>
        DohStubServer.Start(new IPEndPoint(IPAddress.Loopback, port),
            new DnsQueryHandler(DomainCatalog.All, upstream, new DnsCache()));

    private static Socket ExclusiveSocket(SocketType type, ProtocolType protocol, int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, type, protocol) { ExclusiveAddressUse = true };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        return socket;
    }

    [Fact]
    public async Task Udp_answers_game_domain_with_client_id()
    {
        var r = await DnsTestClient.UdpAsync(_server.LocalEndPoint, TestDnsMessage.Query(Name, id: 0x0A0B));

        Assert.Equal(0x0A0B, DnsWire.ReadId(r));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(r));
        Assert.Equal(1, TestDnsMessage.U16(r, 6));
    }

    [Fact]
    public async Task Udp_refuses_other_domains()
    {
        var r = await DnsTestClient.UdpAsync(_server.LocalEndPoint, TestDnsMessage.Query("google.com"));

        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(r));
        Assert.Equal(0, _upstream.Calls);
    }

    [Fact]
    public async Task Tcp_answers_several_queries_on_one_connection()
    {
        await using var stream = await DnsTestClient.ConnectTcpAsync(_server.LocalEndPoint);

        var first = await DnsTestClient.TcpAsync(stream, TestDnsMessage.Query(Name, id: 1));
        var second = await DnsTestClient.TcpAsync(stream, TestDnsMessage.Query("steamcommunity.com", id: 2));

        Assert.Equal(1, DnsWire.ReadId(first));
        Assert.Equal(2, DnsWire.ReadId(second));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(second));
    }

    [Fact]
    public async Task Large_answer_is_truncated_on_udp_and_complete_on_tcp()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);
        var query = TestDnsMessage.Query(Name);

        var udp = await DnsTestClient.UdpAsync(_server.LocalEndPoint, query);
        await using var stream = await DnsTestClient.ConnectTcpAsync(_server.LocalEndPoint);
        var tcp = await DnsTestClient.TcpAsync(stream, query);

        Assert.Equal(0x02, udp[2] & 0x02);
        Assert.Equal(0, tcp[2] & 0x02);
        Assert.Equal(40, TestDnsMessage.U16(tcp, 6));
    }

    [Fact]
    public async Task Survives_garbage_and_clients_that_left()
    {
        using (var gone = new UdpClient(AddressFamily.InterNetwork))
        {
            gone.Connect(_server.LocalEndPoint);
            await gone.SendAsync(Junk);
            await gone.SendAsync(TestDnsMessage.Query(Name));
        } // đóng trước khi nhận: gói trả lời làm Windows báo ICMP port unreachable ở phía stub

        var r = await DnsTestClient.UdpAsync(_server.LocalEndPoint, TestDnsMessage.Query("steamcommunity.com", id: 7));

        Assert.Equal(7, DnsWire.ReadId(r));
    }

    [Fact]
    public void Start_fails_with_PortInUse_when_udp_port_is_taken()
    {
        using var blocker = ExclusiveSocket(SocketType.Dgram, ProtocolType.Udp, 0);
        var port = ((IPEndPoint)blocker.LocalEndPoint!).Port;

        var ex = Assert.Throws<DnsStubException>(() => StartOn(port, _upstream));

        Assert.Equal(DnsStubError.PortInUse, ex.Error);
    }

    [Fact]
    public void Failed_start_releases_udp_port_when_tcp_port_is_taken()
    {
        using var blocker = ExclusiveSocket(SocketType.Stream, ProtocolType.Tcp, 0);
        var port = ((IPEndPoint)blocker.LocalEndPoint!).Port;

        Assert.Throws<DnsStubException>(() => StartOn(port, _upstream));

        using var probe = ExclusiveSocket(SocketType.Dgram, ProtocolType.Udp, port); // ném nếu stub còn giữ UDP
    }

    [Fact]
    public void Start_rejects_non_loopback_endpoint() =>
        Assert.Throws<ArgumentException>(() => DohStubServer.Start(new IPEndPoint(IPAddress.Any, 0),
            new DnsQueryHandler(DomainCatalog.All, _upstream, new DnsCache())));

    [Fact]
    public async Task Udp_drops_packets_beyond_in_flight_limit()
    {
        var upstream = new GatedUpstream();
        await using var server = DohStubServer.Start(new IPEndPoint(IPAddress.Loopback, 0),
            new DnsQueryHandler(DomainCatalog.All, upstream, new DnsCache()), logger: null, maxUdpInFlight: 1);
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Connect(server.LocalEndPoint);

        await client.SendAsync(TestDnsMessage.Query(Name, id: 1));
        await upstream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.SendAsync(TestDnsMessage.Query("google.com", id: 2)); // khe duy nhất đang bận: bị bỏ
        await Task.Delay(300);
        upstream.Release.SetResult();

        using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = await client.ReceiveAsync(wait.Token);
        Assert.Equal(1, DnsWire.ReadId(first.Buffer));
        using var none = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await client.ReceiveAsync(none.Token));
    }

    private sealed class GatedUpstream : IDnsUpstream
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return FakeUpstream.Answer()(query.ToArray());
        }
    }

    [Fact]
    public async Task Dispose_stops_loops_and_releases_port()
    {
        var server = StartOn(0, _upstream);
        var port = server.LocalEndPoint.Port;

        await server.DisposeAsync();

        Assert.True(server.Completion.IsCompletedSuccessfully);
        using var probe = ExclusiveSocket(SocketType.Dgram, ProtocolType.Udp, port);
    }
}
