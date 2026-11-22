using System.Text.Json;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Journal;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Tests.Platform;

public class OrphanSweeperTests
{
    private static readonly string[] Namespaces = ["steampowered.com"];
    private static readonly string[] SweepThenFlush = ["sweep", "flush"];
    private static readonly string[] SweepOnly = ["sweep"];

    private readonly InMemoryJournalStore _store = new();
    private readonly FakeConfigurator _cfg = new();

    private ChangeJournal Journal() => new(_store, SystemChanges.Reverters(_cfg));

    [Fact]
    public void Passes_what_the_journal_still_holds()
    {
        var journal = Journal();
        journal.Record("dns", ChangeOps.DnsRoute, new DnsRouteSnapshot("LunaDNS kept", Namespaces, "127.0.0.1", []),
            SnapshotJsonContext.Default.DnsRouteSnapshot);

        OrphanSweeper.Sweep(journal, _cfg, NullLogger.Instance);

        Assert.Equal(["LunaDNS kept"], _cfg.SweptWith!.DnsMarkers);
    }

    [Fact]
    public void Flushes_dns_cache_once_when_dns_or_hosts_changed()
    {
        _cfg.SweepResult = new OrphanSweepResult(DnsRoutes: 2, AutoProxies: 0, HostsLines: 1, []);

        var result = OrphanSweeper.Sweep(Journal(), _cfg, NullLogger.Instance);

        Assert.Equal(3, result.Removed);
        Assert.Equal(SweepThenFlush, _cfg.Calls);
    }

    [Fact]
    public void Does_not_flush_when_only_pac_changed_or_nothing()
    {
        _cfg.SweepResult = new OrphanSweepResult(0, 1, 0, []);

        OrphanSweeper.Sweep(Journal(), _cfg, NullLogger.Instance);

        Assert.Equal(SweepOnly, _cfg.Calls);
    }

    [Fact]
    public void Configurator_exception_becomes_a_failure_not_a_crash()
    {
        _cfg.SweepThrows = true;

        var result = OrphanSweeper.Sweep(Journal(), _cfg, NullLogger.Instance);

        Assert.False(result.Success);
    }

    [Fact]
    public void RunParts_isolates_each_kind_and_skips_unknown_kinds()
    {
        var coverage = JournalCoverage.From(
            [new JournalEntry(Guid.NewGuid(), "f", ChangeOps.HostsEntries, JsonDocument.Parse("1").RootElement.Clone(), DateTimeOffset.UnixEpoch)]);
        var hostsCalled = false;

        var result = OrphanSweeper.RunParts(coverage,
            dns: _ => throw new IOException("giả lập"),
            proxy: _ => 2,
            hosts: _ =>
            {
                hostsCalled = true;
                return 1;
            });

        Assert.False(hostsCalled);
        Assert.Equal(0, result.DnsRoutes);
        Assert.Equal(2, result.AutoProxies);
        Assert.Equal(ChangeOps.DnsRoute, Assert.Single(result.Failed).Op);
    }

    [Fact]
    public void RunParts_with_null_part_does_nothing_for_that_kind()
    {
        var result = OrphanSweeper.RunParts(JournalCoverage.From([]), dns: _ => 1, proxy: null, hosts: null);

        Assert.Equal(1, result.Removed);
        Assert.True(result.Success);
    }
}
