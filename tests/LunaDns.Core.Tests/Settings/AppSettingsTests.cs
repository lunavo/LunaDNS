using LunaDns.Core.Features;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Settings;

public sealed class AppSettingsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var settings = new AppSettings();

        Assert.Equal(SplitMode.FirstByte, settings.SplitMode);
        Assert.Equal(0, settings.SplitDelayMs);
        Assert.False(settings.PreferGoogleDoh);
        Assert.False(settings.CheckPubg);
        Assert.False(settings.PubgWarningAccepted);
        Assert.False(settings.DomainsIntroSeen);
        Assert.Equal(FeatureTuning.Default, settings.Tuning());
    }

    [Theory]
    [InlineData(false, "Cloudflare,Google")]
    [InlineData(true, "Google,Cloudflare")]
    public void Doh_order_follows_preference(bool preferGoogle, string expected)
    {
        var settings = new AppSettings { PreferGoogleDoh = preferGoogle };

        Assert.Equal(expected, string.Join(",", settings.DohServers().Select(s => s.Name)));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(7, 7)]
    [InlineData(50, 20)]
    public void Tuning_clamps_delay_to_0_20_ms(int input, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), new AppSettings { SplitDelayMs = input }.Tuning().SplitDelay);
}
