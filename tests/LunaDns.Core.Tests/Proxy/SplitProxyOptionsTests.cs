using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Proxy;

public class SplitProxyOptionsTests
{
    [Fact]
    public void Defaults_follow_spec_4_3()
    {
        var options = new SplitProxyOptions { Domains = DomainCatalog.All };

        Assert.Equal(18080, options.FirstPort);
        Assert.Equal(18090, options.LastPort);
        Assert.Equal(SplitMode.FirstByte, options.SplitMode);
        Assert.Equal(TimeSpan.Zero, options.SplitDelay);
        options.Validate();
    }

    [Theory]
    [InlineData(18090, 18080, 0)]
    [InlineData(18080, 70000, 0)]
    [InlineData(-1, 18090, 0)]
    [InlineData(18080, 18090, 21)]
    [InlineData(18080, 18090, -1)]
    public void Validate_rejects_bad_values(int firstPort, int lastPort, int delayMs)
    {
        var options = new SplitProxyOptions
        {
            Domains = DomainCatalog.All,
            FirstPort = firstPort,
            LastPort = lastPort,
            SplitDelay = TimeSpan.FromMilliseconds(delayMs),
        };

        Assert.Throws<ArgumentException>(options.Validate);
    }
}
