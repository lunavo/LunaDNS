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
}
