using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

public class ProbeTargetsTests
{
    private static readonly string[] SpecSteamHosts =
    [
        "store.steampowered.com", "steamcommunity.com", "api.steampowered.com", "cdn.akamai.steamstatic.com",
    ];

    [Fact]
    public void Steam_hosts_are_the_ones_in_spec_4_1() =>
        Assert.Equal(SpecSteamHosts, ProbeTargets.Steam);

    [Fact]
    public void Pubg_host_is_included_only_on_request()
    {
        string[] withPubg = [.. SpecSteamHosts, "prod-live-front.playbattlegrounds.com"];

        Assert.Equal(SpecSteamHosts, ProbeTargets.For(includePubg: false));
        Assert.Equal(withPubg, ProbeTargets.For(includePubg: true));
    }

    [Fact]
    public void Every_target_is_a_game_domain() =>
        Assert.All(ProbeTargets.For(includePubg: true), host => Assert.True(DomainMatcher.Matches(host, GameDomains.All)));
}
