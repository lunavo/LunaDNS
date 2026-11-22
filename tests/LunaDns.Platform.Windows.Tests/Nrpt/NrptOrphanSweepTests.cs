using System.Net;
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.Platform.Windows.Tests.Nrpt;

public class NrptOrphanSweepTests
{
    private static readonly string[] Steam = ["steampowered.com"];
    private static readonly string[] Community = ["steamcommunity.com"];
    private static readonly string[] Loopback = ["127.0.0.1"];
    private static readonly HashSet<string> None = [];

    private readonly FakeNrptStore _store = new();
    private readonly NrptRule _tailscale = FakeNrptStore.Tailscale();
    private readonly NrptDnsRouter _router;

    public NrptOrphanSweepTests()
    {
        _store.Rules.Add(_tailscale);
        _router = new NrptDnsRouter(_store);
    }

    [Fact]
    public void Removes_lunadns_rules_the_journal_does_not_hold_and_keeps_tailscale()
    {
        _router.Apply(_router.Plan(Steam, IPAddress.Loopback));
        _router.Apply(_router.Plan(Community, IPAddress.Loopback));

        Assert.Equal(2, _router.SweepOrphans(None));
        Assert.Same(_tailscale, Assert.Single(_store.Rules));
    }

    [Fact]
    public void Keeps_rules_whose_marker_the_journal_still_holds()
    {
        var kept = _router.Plan(Steam, IPAddress.Loopback);
        _router.Apply(kept);
        _router.Apply(_router.Plan(Community, IPAddress.Loopback));
        HashSet<string> covered = [kept.Marker];

        Assert.Equal(1, _router.SweepOrphans(covered));
        Assert.Contains(_store.Rules, r => r.Comment == kept.Marker);
        Assert.Equal(2, _store.Rules.Count);
    }

    [Theory]
    [InlineData("LunaDNS ")]      // dấu thiếu id
    [InlineData("LunaDNSabc")]
    [InlineData("lunadns abc")]
    [InlineData("Surfshark")]
    public void Never_touches_foreign_rules(string comment)
    {
        _store.Rules.Add(new NrptRule("{11111111-1111-1111-1111-111111111111}", Steam, Loopback, comment, "LunaDNS"));

        Assert.Equal(0, _router.SweepOrphans(None));
        Assert.Equal(2, _store.Rules.Count);
    }
}
