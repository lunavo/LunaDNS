using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DohClientNetworkTests
{
    [NetworkFact]
    public async Task Each_default_server_answers_via_bootstrap_ip()
    {
        foreach (var server in DohServer.Defaults)
        {
            using var client = DohClient.Create([server]);
            var response = await client.QueryAsync(TestDnsMessage.Query("store.steampowered.com"), CancellationToken.None);

            Assert.Equal(DnsRcode.NoError, DnsWire.ReadRcode(response));
            Assert.NotNull(DnsWire.GetMinTtl(response));
        }
    }
}
