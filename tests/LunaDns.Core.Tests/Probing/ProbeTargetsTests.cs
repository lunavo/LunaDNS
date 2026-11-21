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
        Assert.All(ProbeTargets.For(includePubg: true), host => Assert.True(DomainMatcher.Matches(host, DomainCatalog.All)));

    [Fact]
    public void Default_selection_probes_only_steam() =>
        Assert.Equal(ProbeTargets.Steam, ProbeTargets.For(new DomainSelection()));

    [Fact]
    public void Selection_adds_group_targets_and_first_five_enabled_custom_domains()
    {
        var custom = Enumerable.Range(0, 7).Select(i => new CustomDomain($"d{i}.example.org", Enabled: i != 0)).ToArray();
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom], Custom = custom };

        var targets = ProbeTargets.For(selection);

        Assert.Equal([.. ProbeTargets.For(DomainGroup.Reddit), "d1.example.org", "d2.example.org", "d3.example.org", "d4.example.org", "d5.example.org"], targets);
    }

    [Fact]
    public void Group_of_maps_hosts_back_to_their_group()
    {
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.Reddit, DomainGroup.Custom], Custom = [new("example.org")] };

        Assert.Equal(DomainGroup.Steam, ProbeTargets.GroupOf("store.steampowered.com", selection));
        Assert.Equal(DomainGroup.Reddit, ProbeTargets.GroupOf("i.redd.it", selection));
        Assert.Equal(DomainGroup.Custom, ProbeTargets.GroupOf("example.org", selection));
    }
}
