using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainCatalogTests
{
    [Fact]
    public void Selectable_groups_are_steam_other_games_reddit() =>
        Assert.Equal([DomainGroup.Steam, DomainGroup.OtherGames, DomainGroup.Reddit], DomainCatalog.Selectable);

    [Fact]
    public void Custom_group_has_no_built_in_domains() =>
        Assert.Empty(DomainCatalog.For(DomainGroup.Custom));

    [Fact]
    public void Steam_group_is_unchanged_from_v1() =>
        Assert.Equal(
            ["steampowered.com", "steamcommunity.com", "steamstatic.com", "steam-chat.com",
             "steamusercontent.com", "steamgames.com", "steamcontent.com", "steamserver.net",
             "counter-strike.net", "dota2.com", "valvesoftware.com"],
            DomainCatalog.For(DomainGroup.Steam));

    [Theory]
    [InlineData(DomainGroup.Steam)]
    [InlineData(DomainGroup.OtherGames)]
    [InlineData(DomainGroup.Reddit)]
    public void Every_selectable_group_has_domains_and_probe_targets_inside_it(DomainGroup group)
    {
        Assert.NotEmpty(DomainCatalog.For(group));
        Assert.NotEmpty(ProbeTargets.For(group));
        foreach (var host in ProbeTargets.For(group))
            Assert.True(DomainMatcher.Matches(host, DomainCatalog.For(group)), host);
    }

    [Fact]
    public void Catalog_never_contains_anticheat() =>
        Assert.DoesNotContain(DomainCatalog.All, d => d.Contains("xenuine", StringComparison.Ordinal) || d == "accounts.pubg.com");
}
