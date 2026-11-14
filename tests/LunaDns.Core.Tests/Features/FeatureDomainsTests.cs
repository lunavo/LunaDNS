using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureDomainsTests
{
    [Fact]
    public void Routed_is_the_steam_group() =>
        Assert.Equal(GameDomains.For(DomainGroup.Steam), FeatureDomains.Routed);

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
}
