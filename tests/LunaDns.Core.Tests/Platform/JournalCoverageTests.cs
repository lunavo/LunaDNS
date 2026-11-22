using System.Text.Json;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Tests.Platform;

public class JournalCoverageTests
{
    private static readonly string[] SteamNamespaces = ["steampowered.com"];
    private static readonly string[] HostsLines = ["127.0.0.1 pubg.com # LunaDNS"];

    private static JournalEntry Entry<T>(string op, T snapshot) =>
        new(Guid.NewGuid(), "f", op, JsonSerializer.SerializeToElement(snapshot), DateTimeOffset.UnixEpoch);

    private static JournalEntry Raw(string op, string json) =>
        new(Guid.NewGuid(), "f", op, JsonDocument.Parse(json).RootElement.Clone(), DateTimeOffset.UnixEpoch);

    [Fact]
    public void Empty_journal_covers_nothing()
    {
        var c = JournalCoverage.From([]);

        Assert.Empty(c.DnsMarkers);
        Assert.Empty(c.PacUrls);
        Assert.Empty(c.HostsLines);
        Assert.Empty(c.Unknown);
    }

    [Fact]
    public void Collects_markers_urls_and_lines_from_remaining_entries()
    {
        var c = JournalCoverage.From(
        [
            Entry(ChangeOps.DnsRoute, new DnsRouteSnapshot("LunaDNS abc", SteamNamespaces, "127.0.0.1", [])),
            Entry(ChangeOps.AutoProxy, new AutoProxySnapshot("", false, null, false, "http://127.0.0.1:18081/proxy.pac", [])),
            Entry(ChangeOps.HostsEntries, new HostsSnapshot(HostsLines, "\n", false)),
        ]);

        Assert.Equal(["LunaDNS abc"], c.DnsMarkers);
        Assert.Equal(["http://127.0.0.1:18081/proxy.pac"], c.PacUrls);
        Assert.Equal(HostsLines, c.HostsLines);
        Assert.Empty(c.Unknown);
    }

    [Fact]
    public void Unreadable_snapshot_marks_only_its_kind_unknown()
    {
        var c = JournalCoverage.From(
        [
            Raw(ChangeOps.DnsRoute, "42"),
            Entry(ChangeOps.HostsEntries, new HostsSnapshot(HostsLines, "\n", false)),
        ]);

        Assert.Equal([ChangeOps.DnsRoute], c.Unknown);
        Assert.Equal(HostsLines, c.HostsLines);
    }

    [Fact]
    public void Missing_snapshot_marks_its_kind_unknown()
    {
        var c = JournalCoverage.From([new JournalEntry(Guid.NewGuid(), "f", ChangeOps.AutoProxy, default, DateTimeOffset.UnixEpoch)]);

        Assert.Equal([ChangeOps.AutoProxy], c.Unknown);
    }

    [Fact]
    public void Unknown_op_from_a_newer_version_is_ignored()
    {
        var c = JournalCoverage.From([Raw("firewall-rule", "{}")]);

        Assert.Empty(c.Unknown);
    }
}
