using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class HostResolverTests
{
    private const string Name = "store.steampowered.com";
    private static readonly IPAddress Sample = IPAddress.Parse("93.184.216.34");

    private readonly FakeUpstream _upstream = new();

    [Fact]
    public async Task Doh_returns_addresses_from_answer()
    {
        var resolver = new DohHostResolver(_upstream);

        var addresses = await resolver.ResolveAsync("Store.SteamPowered.com.", CancellationToken.None);

        Assert.Equal(Sample, Assert.Single(addresses));
    }

    [Fact]
    public async Task Doh_returns_empty_for_NXDOMAIN()
    {
        _upstream.Respond = _ => TestDnsMessage.NxDomain(Name, 0, 60);

        Assert.Empty(await new DohHostResolver(_upstream).ResolveAsync(Name, CancellationToken.None));
    }

    [Fact]
    public async Task Doh_throws_on_SERVFAIL()
    {
        _upstream.Respond = q => DnsWire.BuildHeaderOnlyResponse(q, DnsRcode.ServFail);

        await Assert.ThrowsAsync<DohException>(() => new DohHostResolver(_upstream).ResolveAsync(Name, CancellationToken.None));
    }

    [Fact]
    public async Task Doh_throws_when_answer_is_for_another_name()
    {
        _upstream.Respond = _ => TestDnsMessage.Response("other.example", 0, 300);

        await Assert.ThrowsAsync<DohException>(() => new DohHostResolver(_upstream).ResolveAsync(Name, CancellationToken.None));
    }

    [Fact]
    public async Task Doh_with_cache_asks_upstream_once()
    {
        var resolver = new DohHostResolver(_upstream, new DnsCache());

        await resolver.ResolveAsync(Name, CancellationToken.None);
        var second = await resolver.ResolveAsync(Name, CancellationToken.None);

        Assert.Equal(1, _upstream.Calls);
        Assert.Equal(Sample, Assert.Single(second));
    }

    [Fact]
    public async Task Doh_without_cache_asks_every_time()
    {
        var resolver = new DohHostResolver(_upstream);

        await resolver.ResolveAsync(Name, CancellationToken.None);
        await resolver.ResolveAsync(Name, CancellationToken.None);

        Assert.Equal(2, _upstream.Calls);
    }

    [Fact]
    public async Task System_resolves_localhost_to_loopback() =>
        Assert.Contains(IPAddress.Loopback, await new SystemHostResolver().ResolveAsync("localhost", CancellationToken.None));

    [Fact]
    public async Task System_returns_ip_literal_as_is() =>
        Assert.Equal(Sample, Assert.Single(await new SystemHostResolver().ResolveAsync("93.184.216.34", CancellationToken.None)));
}
