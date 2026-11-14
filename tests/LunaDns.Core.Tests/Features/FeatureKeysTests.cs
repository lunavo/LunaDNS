using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureKeysTests
{
    // Giá trị nằm trong journal.json trên máy người dùng: đổi tên là mất khả năng hoàn tác mục cũ.
    [Theory]
    [InlineData(Feature.DnsRouting, "dns-routing")]
    [InlineData(Feature.SniProxy, "sni-proxy")]
    [InlineData(Feature.PubgMode, "pubg-mode")]
    public void Keys_are_stable(Feature feature, string expected) => Assert.Equal(expected, FeatureKeys.For(feature));
}
