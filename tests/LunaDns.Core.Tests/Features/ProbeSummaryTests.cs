using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Features;

public sealed class ProbeSummaryTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly FeatureStatus AllOff = new(false, false, false, null, 0);

    private static ProbeResult Result(string host, ProbeVerdict verdict) => new(host, verdict, [], [], false);

    [Fact]
    public void Groups_steam_and_pubg_hosts()
    {
        var summary = ProbeSummary.Create(
            [Result("store.steampowered.com", ProbeVerdict.Ok), Result("prod-live-front.playbattlegrounds.com", ProbeVerdict.SniFiltered)], At);

        Assert.Equal(2, summary.Groups.Count);
        Assert.Equal(DomainGroup.Steam, summary.Groups[0].Group);
        Assert.Equal("store.steampowered.com", Assert.Single(summary.Groups[0].Results).Host);
        Assert.Equal(DomainGroup.Pubg, summary.Groups[1].Group);
        Assert.Equal(At, summary.CheckedAt);
    }

    [Fact]
    public void Without_pubg_results_there_is_one_group() =>
        Assert.Single(ProbeSummary.Create([Result("steamcommunity.com", ProbeVerdict.Ok)], At).Groups);

    [Fact]
    public void Worst_is_the_heaviest_verdict()
    {
        var summary = ProbeSummary.Create(
            [Result("a.steampowered.com", ProbeVerdict.DnsPoisoned), Result("b.steampowered.com", ProbeVerdict.SniFiltered), Result("c.steampowered.com", ProbeVerdict.Ok)], At);

        Assert.Equal(ProbeVerdict.SniFiltered, summary.Groups[0].Worst);
    }

    [Fact]
    public void Pending_remedies_drop_features_already_on()
    {
        var summary = ProbeSummary.Create([Result("store.steampowered.com", ProbeVerdict.SniFiltered)], At);

        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, summary.PendingSteamRemedies(AllOff));
        Assert.Equal(ProbeRemedies.SniProxy, summary.PendingSteamRemedies(AllOff with { DnsRouting = true }));
    }

    [Fact]
    public void Suggests_pubg_mode_only_when_blocked_and_off()
    {
        var blocked = ProbeSummary.Create([Result("prod-live-front.playbattlegrounds.com", ProbeVerdict.DnsPoisoned)], At);
        var fine = ProbeSummary.Create([Result("prod-live-front.playbattlegrounds.com", ProbeVerdict.Ok)], At);

        Assert.True(blocked.SuggestPubgMode(AllOff));
        Assert.False(blocked.SuggestPubgMode(AllOff with { PubgMode = true }));
        Assert.False(fine.SuggestPubgMode(AllOff));
    }

    [Fact]
    public void Any_unreachable_is_reported()
    {
        Assert.True(ProbeSummary.Create([Result("store.steampowered.com", ProbeVerdict.Unreachable)], At).AnyUnreachable);
        Assert.False(ProbeSummary.Create([Result("store.steampowered.com", ProbeVerdict.Ok)], At).AnyUnreachable);
    }

    [Fact]
    public void Status_remedies_follow_the_two_switches()
    {
        Assert.Equal(ProbeRemedies.None, AllOff.Remedies);
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, new FeatureStatus(true, true, true, 18080, 0).Remedies);
    }

    /// <summary>Bảng "Dòng kết quả kiểm tra" ở KH6 mục 3.2, thêm dòng SNI khi cả hai biện pháp còn tắt.</summary>
    [Theory]
    [InlineData(ProbeVerdict.Ok, false, false, ProbeCoverageState.NotNeeded, ProbeRemedies.None, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.DnsPoisoned, false, false, ProbeCoverageState.Missing, ProbeRemedies.DnsRouting, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.DnsPoisoned, true, false, ProbeCoverageState.Covered, ProbeRemedies.None, ProbeRemedies.DnsRouting)]
    [InlineData(ProbeVerdict.SniFiltered, false, false, ProbeCoverageState.Missing, ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.SniFiltered, true, false, ProbeCoverageState.Missing, ProbeRemedies.SniProxy, ProbeRemedies.None)]
    [InlineData(ProbeVerdict.SniFiltered, true, true, ProbeCoverageState.Covered, ProbeRemedies.None, ProbeRemedies.SniProxy)]
    [InlineData(ProbeVerdict.Unreachable, true, true, ProbeCoverageState.Unreachable, ProbeRemedies.None, ProbeRemedies.None)]
    public void Coverage_follows_kh6_table(
        ProbeVerdict verdict, bool dns, bool sni, ProbeCoverageState state, ProbeRemedies missing, ProbeRemedies covering)
    {
        var group = new ProbeGroupSummary(DomainGroup.Steam,
            [Result("store.steampowered.com", verdict), Result("steamcommunity.com", ProbeVerdict.Ok)]);

        Assert.Equal(new ProbeCoverage(state, missing, covering), group.Coverage(new FeatureStatus(dns, sni, false, null, 0)));
    }

    [Fact]
    public void Create_with_group_map_keeps_groups_in_first_seen_order()
    {
        var results = new[] { Result("store.steampowered.com", ProbeVerdict.Ok), Result("www.reddit.com", ProbeVerdict.SniFiltered) };

        var summary = ProbeSummary.Create(results, DateTimeOffset.UnixEpoch,
            host => host.Contains("reddit", StringComparison.Ordinal) ? DomainGroup.Reddit : DomainGroup.Steam);

        Assert.Equal([DomainGroup.Steam, DomainGroup.Reddit], summary.Groups.Select(g => g.Group));
        Assert.Equal(ProbeVerdict.SniFiltered, summary.Groups[1].Worst);
    }
}
