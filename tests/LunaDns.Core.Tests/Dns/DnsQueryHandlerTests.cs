using LunaDns.Core.Dns;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Dns;

public class DnsQueryHandlerTests
{
    private const string Name = "store.steampowered.com";
    private readonly FakeUpstream _upstream = new();
    private readonly DnsQueryHandler _handler;

    public DnsQueryHandlerTests() => _handler = new DnsQueryHandler(DomainCatalog.All, _upstream, new DnsCache());

    private Task<byte[]?> Handle(byte[] packet, DnsTransport transport = DnsTransport.Udp) =>
        _handler.HandleAsync(packet, transport, CancellationToken.None);

    private async Task<byte[]> HandleOk(byte[] packet, DnsTransport transport = DnsTransport.Udp)
    {
        var response = await Handle(packet, transport);
        Assert.NotNull(response);
        return response;
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("evilsteampowered.com")]
    public async Task Refuses_names_outside_game_domains_without_asking_upstream(string name)
    {
        var r = await HandleOk(TestDnsMessage.Query(name));

        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(r));
        Assert.Equal(0x1234, DnsWire.ReadId(r));
        Assert.Equal(0, _upstream.Calls);
    }

    [Fact]
    public async Task Forwards_game_domain_and_restores_client_id()
    {
        var r = await HandleOk(TestDnsMessage.Query(Name, id: 0x0A0B));

        Assert.Equal(0x0A0B, DnsWire.ReadId(r));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(r));
        Assert.Equal(1, TestDnsMessage.U16(r, 6));
        Assert.Equal(1, _upstream.Calls);
    }

    [Fact]
    public async Task Second_query_is_served_from_cache()
    {
        await HandleOk(TestDnsMessage.Query(Name, id: 1));
        var r = await HandleOk(TestDnsMessage.Query(Name, id: 2));

        Assert.Equal(2, DnsWire.ReadId(r));
        Assert.Equal(1, _upstream.Calls);
    }

    [Fact]
    public async Task First_answer_is_capped_at_cache_max_ttl()
    {
        _upstream.Respond = FakeUpstream.Answer(ttl: 3600);

        var r = await HandleOk(TestDnsMessage.Query(Name));

        Assert.Equal(DnsCache.MaxTtlSeconds, DnsWire.GetMinTtl(r));
    }

    [Fact]
    public async Task Upstream_failure_returns_servfail()
    {
        _upstream.Respond = _ => throw new DohException("hỏng");

        var r = await HandleOk(TestDnsMessage.Query(Name));

        Assert.Equal(DnsRcode.ServFail, DnsWire.ReadRcode(r));
    }

    [Fact]
    public async Task Malformed_query_returns_formerr()
    {
        var packet = TestDnsMessage.Query(Name);
        packet[5] = 2; // QDCOUNT = 2

        var r = await HandleOk(packet);

        Assert.Equal(DnsRcode.FormErr, DnsWire.ReadRcode(r));
        Assert.Equal(0, _upstream.Calls);
    }

    [Fact]
    public async Task Packet_shorter_than_header_is_dropped() =>
        Assert.Null(await Handle(new byte[5]));

    [Fact]
    public async Task Stray_response_is_dropped() =>
        Assert.Null(await Handle(TestDnsMessage.Response(Name, 1, 300)));

    [Fact]
    public async Task Non_query_opcode_returns_notimp()
    {
        var r = await HandleOk(TestDnsMessage.Query(Name, flags: 0x0100 | (2 << 11)));
        Assert.Equal(DnsRcode.NotImp, DnsWire.ReadRcode(r));
    }

    [Fact]
    public async Task Large_answer_over_plain_udp_is_truncated()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);

        var r = await HandleOk(TestDnsMessage.Query(Name));

        Assert.Equal(0x02, r[2] & 0x02);
        Assert.Equal(0, TestDnsMessage.U16(r, 6));
    }

    [Fact]
    public async Task Large_answer_over_tcp_is_complete()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);

        var r = await HandleOk(TestDnsMessage.Query(Name), DnsTransport.Tcp);

        Assert.Equal(0, r[2] & 0x02);
        Assert.Equal(40, TestDnsMessage.U16(r, 6));
    }

    [Fact]
    public async Task Large_answer_fits_when_edns_allows()
    {
        _upstream.Respond = FakeUpstream.Answer(records: 40);

        var r = await HandleOk(TestDnsMessage.Query(Name, ednsUdpSize: 1232));

        Assert.Equal(40, TestDnsMessage.U16(r, 6));
    }
}
