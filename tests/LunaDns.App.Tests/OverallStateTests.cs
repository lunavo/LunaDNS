using LunaDns.Core.Features;

namespace LunaDns.App.Tests;

/// <summary>Bảng "Trạng thái tổng" ở KH6 mục 3.2 và tooltip khay ở 3.4.</summary>
public sealed class OverallStateTests
{
    private static FeatureStatus Status(bool dns, bool sni, bool pubg = false) => new(dns, sni, pubg, null, 0);

    [Fact]
    public void Nothing_on_is_off()
    {
        var state = OverallState.From(Status(false, false), busy: false, attention: false);

        Assert.Equal(OverallKind.Off, state.Kind);
        Assert.Equal("Đang tắt", state.Text);
        Assert.Equal("LunaDNS: đang tắt", state.Tooltip);
        Assert.Equal("Neutral", state.Tone.ToString());
    }

    [Fact]
    public void One_switch_on_counts_one_of_two()
    {
        var state = OverallState.From(Status(true, false), busy: false, attention: false);

        Assert.Equal(OverallKind.On, state.Kind);
        Assert.Equal("Đang bật 1/2", state.Text);
        Assert.Equal("LunaDNS: đang bật Định tuyến DNS", state.Tooltip);
        Assert.Equal("Good", state.Tone.ToString());
    }

    [Fact]
    public void Both_switches_on_are_listed_in_the_tooltip()
    {
        var state = OverallState.From(Status(true, true), busy: false, attention: false);

        Assert.Equal("Đang bật 2/2", state.Text);
        Assert.Equal("LunaDNS: đang bật Định tuyến DNS, Proxy SNI", state.Tooltip);
    }

    [Fact]
    public void Attention_wins_over_on()
    {
        var state = OverallState.From(Status(true, true), busy: false, attention: true);

        Assert.Equal(OverallKind.Attention, state.Kind);
        Assert.Equal("Cần xử lý", state.Text);
        Assert.Equal("LunaDNS: cần xử lý, mở để xem", state.Tooltip);
        Assert.Equal("Error", state.Tone.ToString());
    }

    [Fact]
    public void Busy_wins_over_attention_and_keeps_the_active_list()
    {
        var state = OverallState.From(Status(true, false), busy: true, attention: true);

        Assert.Equal(OverallKind.Busy, state.Kind);
        Assert.Equal("Đang xử lý…", state.Text);
        Assert.Equal("LunaDNS: đang xử lý…", state.Tooltip);
        Assert.Equal("Busy", state.Tone.ToString());
        Assert.Equal([Feature.DnsRouting], state.Active);
    }

    /// <summary>KH6 U0: Chế độ PUBG ẩn khỏi v1, không tính vào trạng thái tổng.</summary>
    [Fact]
    public void Pubg_mode_alone_is_off() =>
        Assert.Equal(OverallKind.Off, OverallState.From(Status(false, false, pubg: true), busy: false, attention: false).Kind);
}
