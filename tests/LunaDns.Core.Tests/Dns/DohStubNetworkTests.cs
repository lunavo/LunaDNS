using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Dns;

public class DohStubNetworkTests
{
    [NetworkFact]
    public async Task Stub_with_real_doh_resolves_steam_over_udp_and_tcp()
    {
        using var doh = DohClient.Create(DohServer.Defaults);
        var handler = new DnsQueryHandler(DomainCatalog.All, doh, new DnsCache());
        await using var server = DohStubServer.Start(new IPEndPoint(IPAddress.Loopback, 0), handler);

        var udp = await DnsTestClient.UdpAsync(server.LocalEndPoint, TestDnsMessage.Query("store.steampowered.com", id: 0x4242));
        await using var stream = await DnsTestClient.ConnectTcpAsync(server.LocalEndPoint);
        var tcp = await DnsTestClient.TcpAsync(stream, TestDnsMessage.Query("steamcommunity.com", id: 0x4343));
        var refused = await DnsTestClient.UdpAsync(server.LocalEndPoint, TestDnsMessage.Query("google.com"));

        Assert.Equal(0x4242, DnsWire.ReadId(udp));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(udp));
        Assert.True(TestDnsMessage.U16(udp, 6) > 0, "store.steampowered.com phải có ít nhất một bản ghi answer");
        Assert.Equal(0x4343, DnsWire.ReadId(tcp));
        Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(tcp));
        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(refused));
    }
}
