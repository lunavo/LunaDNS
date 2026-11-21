using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.App.Tests;

/// <summary>Bảng "Dòng kết quả kiểm tra" ở KH6 mục 3.2. Tone là kiểu internal nên tham số test là tên của nó.</summary>
public sealed class ProbeLineViewModelTests
{
    [Theory]
    [InlineData(ProbeVerdict.Ok, false, false, "Bình thường", "Good")]
    [InlineData(ProbeVerdict.DnsPoisoned, false, false, "DNS bị chặn · còn thiếu Định tuyến DNS", "Warning")]
    [InlineData(ProbeVerdict.DnsPoisoned, true, false, "DNS bị chặn · đang né bằng Định tuyến DNS", "Good")]
    [InlineData(ProbeVerdict.SniFiltered, false, false, "Bị lọc SNI · còn thiếu Định tuyến DNS, Proxy SNI", "Warning")]
    [InlineData(ProbeVerdict.SniFiltered, true, false, "Bị lọc SNI · còn thiếu Proxy SNI", "Warning")]
    [InlineData(ProbeVerdict.SniFiltered, true, true, "Bị lọc SNI · đang né bằng Proxy SNI", "Good")]
    [InlineData(ProbeVerdict.Unreachable, true, true, "Không vào được", "Error")]
    public void Line_follows_kh6_table(ProbeVerdict verdict, bool dns, bool sni, string detail, string tone)
    {
        var group = new ProbeGroupSummary(DomainGroup.Steam, [new ProbeResult("store.steampowered.com", verdict, [], [], false)]);

        var line = ProbeLineViewModel.From(group, new FeatureStatus(dns, sni, false, null, 0));

        Assert.Equal("Steam", line.Group);
        Assert.Equal(detail, line.Detail);
        Assert.Equal(tone, line.Tone.ToString());
        Assert.Equal($"Steam: {detail}", line.AutomationName);
    }

    [Fact]
    public void Custom_group_counts_helped_and_unhelped_domains()
    {
        var group = new ProbeGroupSummary(DomainGroup.Custom,
        [
            new ProbeResult("a.example.org", ProbeVerdict.SniFiltered, [], [], false),
            new ProbeResult("b.example.org", ProbeVerdict.DnsPoisoned, [], [], false),
            new ProbeResult("c.example.org", ProbeVerdict.Unreachable, [], [], false),
            new ProbeResult("d.example.org", ProbeVerdict.Ok, [], [], false),
        ]);

        var line = ProbeLineViewModel.From(group, new FeatureStatus(true, true, false, null, 0));

        Assert.Equal("Tuỳ chỉnh", line.Group);
        Assert.Equal("2 tên miền giúp được, 1 tên miền LunaDNS không giúp được", line.Detail);
        Assert.Equal("Error", line.Tone.ToString());
    }

    [Fact]
    public void Custom_group_all_ok_uses_normal_line()
    {
        var group = new ProbeGroupSummary(DomainGroup.Custom, [new ProbeResult("a.example.org", ProbeVerdict.Ok, [], [], false)]);

        var line = ProbeLineViewModel.From(group, new FeatureStatus(false, false, false, null, 0));

        Assert.Equal("Bình thường", line.Detail);
        Assert.Equal("Good", line.Tone.ToString());
    }

    [Theory]
    [InlineData(DomainGroup.OtherGames, "Nền tảng game khác")]
    [InlineData(DomainGroup.Reddit, "Reddit")]
    public void New_groups_have_names(DomainGroup group, string name)
    {
        var line = ProbeLineViewModel.From(new ProbeGroupSummary(group, []), new FeatureStatus(false, false, false, null, 0));

        Assert.Equal(name, line.Group);
    }
}
