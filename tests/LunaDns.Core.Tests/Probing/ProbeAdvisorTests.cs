using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

public class ProbeAdvisorTests
{
    private static ProbeResult Result(ProbeVerdict verdict) => new("store.steampowered.com", verdict, [], [], false);

    [Fact]
    public void Nothing_to_do_when_everything_is_ok() =>
        Assert.Equal(ProbeRemedies.None, ProbeAdvisor.Recommend([Result(ProbeVerdict.Ok), Result(ProbeVerdict.Ok)]));

    [Fact]
    public void Poisoned_dns_needs_dns_routing_only() =>
        Assert.Equal(ProbeRemedies.DnsRouting, ProbeAdvisor.Recommend([Result(ProbeVerdict.Ok), Result(ProbeVerdict.DnsPoisoned)]));

    [Fact]
    public void Sni_filtering_needs_dns_routing_and_proxy() =>
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, ProbeAdvisor.Recommend([Result(ProbeVerdict.SniFiltered)]));

    [Fact]
    public void Unreachable_alone_recommends_nothing() =>
        Assert.Equal(ProbeRemedies.None, ProbeAdvisor.Recommend([Result(ProbeVerdict.Unreachable)]));

    [Fact]
    public void Mixed_verdicts_are_combined() =>
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy,
            ProbeAdvisor.Recommend([Result(ProbeVerdict.DnsPoisoned), Result(ProbeVerdict.Unreachable), Result(ProbeVerdict.SniFiltered)]));
}
