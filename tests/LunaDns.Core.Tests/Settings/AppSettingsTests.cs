using LunaDns.Core.Dns;
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
        Assert.False(settings.SplitRecords);
        Assert.False(settings.PreferGoogleDoh);
        Assert.Null(settings.Doh);
        Assert.Null(settings.CustomDoh);
        Assert.False(settings.CheckPubg);
        Assert.False(settings.PubgWarningAccepted);
        Assert.False(settings.DomainsIntroSeen);
        Assert.Equal(FeatureTuning.Default, settings.Tuning());
    }

    [Fact]
    public void Tuning_carries_record_split()
    {
        var settings = new AppSettings { SplitMode = SplitMode.MidSni, SplitRecords = true, SplitDelayMs = 3 };

        Assert.Equal(new FeatureTuning(new HelloSplit(SplitMode.MidSni, Records: true), TimeSpan.FromMilliseconds(3)), settings.Tuning());
    }

    [Theory]
    [InlineData(false, DohProvider.Cloudflare, "Cloudflare,Google,Quad9")]
    [InlineData(true, DohProvider.Google, "Google,Cloudflare,Quad9")]
    public void Old_settings_keep_their_doh_choice(bool preferGoogle, DohProvider expected, string chain)
    {
        var settings = new AppSettings { PreferGoogleDoh = preferGoogle };

        Assert.Null(settings.Doh);
        Assert.Equal(expected, settings.EffectiveDoh());
        Assert.Equal(chain, string.Join(",", settings.DohServers().Select(s => s.Name)));
    }

    [Fact]
    public void Doh_overrides_legacy_preference()
    {
        var settings = new AppSettings { PreferGoogleDoh = true, Doh = DohProvider.Quad9 };

        Assert.Equal(DohProvider.Quad9, settings.EffectiveDoh());
        Assert.Equal("Quad9", settings.DohServers()[0].Name);
    }

    [Theory]
    [InlineData(DohProvider.Google, true)]
    [InlineData(DohProvider.Quad9, false)]
    [InlineData(DohProvider.Cloudflare, false)]
    public void WithDoh_keeps_legacy_flag_in_sync(DohProvider provider, bool preferGoogle)
    {
        var settings = new AppSettings { PreferGoogleDoh = !preferGoogle }.WithDoh(provider);

        Assert.Equal(provider, settings.Doh);
        Assert.Equal(preferGoogle, settings.PreferGoogleDoh);
    }

    [Fact]
    public void Custom_without_address_falls_back_to_cloudflare_chain()
    {
        var settings = new AppSettings { Doh = DohProvider.Custom, CustomDoh = new CustomDoh { Endpoint = "https://dns.example.net/q" } };

        Assert.Equal("Cloudflare,Google,Quad9", string.Join(",", settings.DohServers().Select(s => s.Name)));
        Assert.Equal(4, settings.DohChoices().Count);
    }

    [Fact]
    public void Custom_with_address_leads_the_chain()
    {
        var settings = new AppSettings { Doh = DohProvider.Custom, CustomDoh = new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] } };

        Assert.Equal("Tự nhập,Cloudflare,Google", string.Join(",", settings.DohServers().Select(s => s.Name)));
        Assert.Equal(DohProvider.Custom, settings.DohChoices()[^1].Provider);
    }

    [Theory]
    [InlineData(DohProvider.Quad9, "Quad9,Cloudflare,Google")]
    [InlineData(DohProvider.Custom, "Cloudflare,Google,Quad9")]
    public void Bootstrap_servers_never_include_custom(DohProvider provider, string expected)
    {
        var settings = new AppSettings { Doh = provider, CustomDoh = new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["1.2.3.4"] } };

        Assert.Equal(expected, string.Join(",", settings.BootstrapDohServers().Select(s => s.Name)));
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(7, 7)]
    [InlineData(50, 20)]
    public void Tuning_clamps_delay_to_0_20_ms(int input, int expectedMs) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMs), new AppSettings { SplitDelayMs = input }.Tuning().SplitDelay);
}
