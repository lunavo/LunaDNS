using LunaDns.App.ViewModels;
using LunaDns.Core.Dns;
using LunaDns.Core.Features;

namespace LunaDns.App.Tests.ViewModels;

/// <summary>F4: thẻ Định tuyến DNS hiện máy DoH đang trả lời.</summary>
public sealed class FeatureViewModelTests
{
    private static FeatureViewModel Card(Feature feature) => new(feature, _ => Task.CompletedTask, _ => { });

    private static FeatureStatus Dns(DohActivity? doh) => new(true, false, false, null, 0, doh);

    [Fact]
    public void Dns_card_shows_primary_doh_server()
    {
        var card = Card(Feature.DnsRouting);

        card.Update(Dns(new DohActivity("Cloudflare", Fallback: false)));

        Assert.Equal("Đang bật · DoH: Cloudflare", card.StatusText);
    }

    [Fact]
    public void Dns_card_marks_fallback_server()
    {
        var card = Card(Feature.DnsRouting);

        card.Update(Dns(new DohActivity("Google", Fallback: true)));

        Assert.Equal("Đang bật · DoH: Google (dự phòng)", card.StatusText);
    }

    [Fact]
    public void Dns_card_without_answer_yet_says_on()
    {
        var card = Card(Feature.DnsRouting);

        card.Update(Dns(null));

        Assert.Equal(Strings.StatusOn, card.StatusText);
    }

    [Fact]
    public void Dns_card_off_ignores_doh()
    {
        var card = Card(Feature.DnsRouting);

        card.Update(new FeatureStatus(false, false, false, null, 0, new DohActivity("Google", Fallback: true)));

        Assert.Equal(Strings.StatusOff, card.StatusText);
    }

    [Fact]
    public void Timer_refresh_picks_up_server_change()
    {
        var card = Card(Feature.DnsRouting);
        card.Update(Dns(new DohActivity("Cloudflare", Fallback: false)));

        card.UpdateStatusText(Dns(new DohActivity("Google", Fallback: true)));

        Assert.Equal("Đang bật · DoH: Google (dự phòng)", card.StatusText);
    }
}
