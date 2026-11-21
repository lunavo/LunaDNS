using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureDomainsTests
{
    [Fact]
    public void Routed_default_is_the_steam_group() =>
        Assert.Equal(DomainCatalog.For(DomainGroup.Steam), FeatureDomains.Routed(new DomainSelection()));

    [Fact]
    public void Routed_ignores_groups_no_longer_selectable()
    {
        // settings.json cũ có thể còn bật OtherGames (bỏ ở Task 13) hoặc Pubg.
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Steam, DomainGroup.OtherGames, DomainGroup.Pubg] };
        Assert.Equal(DomainCatalog.For(DomainGroup.Steam), FeatureDomains.Routed(selection));
    }

    [Fact]
    public void Pubg_hosts_are_loopback_pubg_hosts_without_anticheat()
    {
        Assert.NotEmpty(FeatureDomains.PubgHosts);
        foreach (var entry in FeatureDomains.PubgHosts)
        {
            Assert.Equal("127.0.0.1", entry.Address);
            Assert.True(DomainMatcher.Matches(entry.Host, FeatureDomains.Pubg), entry.Host);
            Assert.DoesNotContain("xenuine", entry.Host, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual("accounts.pubg.com", entry.Host);
        }
    }

    [Fact]
    public void Routed_includes_layer_two_of_enabled_groups_only()
    {
        var catalog = EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>>
        {
            [DomainGroup.Steam] = ["cdn.example.net"],
            [DomainGroup.Reddit] = ["redditblog.com"],
        }, DateTimeOffset.UnixEpoch);

        var routed = FeatureDomains.Routed(new DomainSelection(), catalog);

        Assert.Contains("cdn.example.net", routed);
        Assert.DoesNotContain("redditblog.com", routed);
    }

    [Fact]
    public void Custom_domain_also_in_layer_two_is_routed_once()
    {
        var catalog = EffectiveCatalog.From(new Dictionary<DomainGroup, IReadOnlyList<string>> { [DomainGroup.Steam] = ["cdn.example.net"] }, DateTimeOffset.UnixEpoch);
        var selection = new DomainSelection { Custom = [new CustomDomain("cdn.example.net")] };

        Assert.Single(FeatureDomains.Routed(selection, catalog), d => d == "cdn.example.net");
    }
}
