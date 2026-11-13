using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public class DomainMatcherTests
{
    private static readonly string[] Domains = ["steampowered.com", "steamcommunity.com"];

    [Theory]
    [InlineData("steampowered.com")]
    [InlineData("store.steampowered.com")]
    [InlineData("STORE.SteamPowered.COM")]
    [InlineData("store.steampowered.com.")]
    [InlineData("a.b.steamcommunity.com")]
    public void Matches_domain_and_subdomains(string host) =>
        Assert.True(DomainMatcher.Matches(host, Domains));

    [Theory]
    [InlineData("evilsteampowered.com")]
    [InlineData("steampowered.com.evil.net")]
    [InlineData("google.com")]
    [InlineData("")]
    [InlineData(".")]
    public void Does_not_match_lookalikes_or_others(string host) =>
        Assert.False(DomainMatcher.Matches(host, Domains));

    [Fact]
    public void Normalize_lowercases_and_trims_trailing_dot() =>
        Assert.Equal("store.steampowered.com", DomainMatcher.Normalize(" Store.SteamPowered.com. "));

    [Fact]
    public void Steam_group_contains_core_domains()
    {
        var steam = GameDomains.For(DomainGroup.Steam);
        Assert.Contains("steampowered.com", steam);
        Assert.Contains("steamcommunity.com", steam);
        Assert.Contains("steamstatic.com", steam);
    }

    [Fact]
    public void No_group_contains_anti_cheat_domains()
    {
        Assert.DoesNotContain(GameDomains.All, d => d.Contains("xenuine", StringComparison.Ordinal));
        Assert.DoesNotContain("accounts.pubg.com", GameDomains.All);
    }

    [Fact]
    public void All_is_union_of_groups_without_duplicates()
    {
        var expected = Enum.GetValues<DomainGroup>().SelectMany(GameDomains.For).Distinct().Count();
        Assert.Equal(expected, GameDomains.All.Count);
    }
}
