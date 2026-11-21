using LunaDns.App;
using LunaDns.Core.Probing;

namespace LunaDns.App.Tests;

public sealed class DomainProbeTextTests
{
    [Theory]
    [InlineData(ProbeVerdict.Ok, "Không bị chặn, không cần LunaDNS")]
    [InlineData(ProbeVerdict.DnsPoisoned, "Giúp được (cần Định tuyến DNS)")]
    [InlineData(ProbeVerdict.SniFiltered, "Giúp được (cần Proxy SNI)")]
    [InlineData(ProbeVerdict.Unreachable, "Chặn theo IP hoặc cách khác, LunaDNS không giúp được")]
    public void Verdict_text_matches_spec(ProbeVerdict verdict, string expected) =>
        Assert.Equal(expected, DomainProbeText.For(verdict));

    [Fact]
    public void Failure_text_matches_spec() =>
        Assert.Equal("Không kiểm tra được, thử lại", DomainProbeText.Failed);
}
