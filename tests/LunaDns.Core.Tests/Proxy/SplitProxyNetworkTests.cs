using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;

namespace LunaDns.Core.Tests.Proxy;

public class SplitProxyNetworkTests
{
    [NetworkFact]
    public async Task Https_to_steam_goes_through_and_other_hosts_are_refused()
    {
        using var doh = DohClient.Create(DohServer.Defaults);
        var connector = new TcpUpstreamConnector(new DohHostResolver(doh, new DnsCache()));
        await using var proxy = SplitProxy.Start(
            new SplitProxyOptions { Domains = DomainCatalog.All, FirstPort = 0, LastPort = 0 }, connector);
        using var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy(new Uri($"http://127.0.0.1:{proxy.Port}")),
            UseProxy = true,
            AllowAutoRedirect = false,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

        using var response = await http.GetAsync(new Uri("https://store.steampowered.com/"));

        Assert.True((int)response.StatusCode < 500, $"Steam trả {(int)response.StatusCode}");
        await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync(new Uri("https://example.com/")));
    }
}
