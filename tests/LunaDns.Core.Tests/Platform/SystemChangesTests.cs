using System.Net;
using System.Text.Json;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Journal;

namespace LunaDns.Core.Tests.Platform;

public class SystemChangesTests
{
    private static readonly string[] SteamDomains = ["steampowered.com"];
    private static readonly HostsEntry[] PubgHosts = [new("127.0.0.1", "pubg.com")];
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");
    private static readonly string[] AllOps = [ChangeOps.DnsRoute, ChangeOps.AutoProxy, ChangeOps.HostsEntries];
    private static readonly string[] ProxyRevertCall = ["revert:proxy:http://127.0.0.1:18080/proxy.pac"];
    private static readonly string[] ApplyCalls =
        ["plan:dns", "apply:dns", "flush", "plan:proxy", "apply:proxy", "plan:hosts", "apply:hosts", "flush"];
    private static readonly string[] RevertCalls =
    [
        "revert:hosts:127.0.0.1 pubg.com", "flush",
        "revert:proxy:http://127.0.0.1:18080/proxy.pac",
        "revert:dns:LunaDNS test", "flush",
    ];

    private readonly InMemoryJournalStore _store = new();
    private readonly FakeConfigurator _cfg = new();

    private ChangeJournal NewJournal() => new(_store, SystemChanges.Reverters(_cfg));

    private static void ApplyAll(SystemChanges changes)
    {
        changes.AddDnsRoute("dns", SteamDomains, IPAddress.Loopback);
        changes.SetAutoProxy("proxy", PacUrl);
        changes.AddHostsEntries("pubg", PubgHosts);
    }

    [Fact]
    public void Records_journal_entry_before_each_apply()
    {
        var journal = NewJournal();
        var expected = 0;
        _cfg.BeforeApply = () => Assert.Equal(++expected, journal.Entries.Count);

        ApplyAll(new SystemChanges(journal, _cfg));

        Assert.Equal(3, expected);
        Assert.Equal(AllOps, journal.Entries.Select(e => e.Op));
    }

    [Fact]
    public void Flushes_dns_cache_after_dns_route_and_hosts_only()
    {
        ApplyAll(new SystemChanges(NewJournal(), _cfg));

        Assert.Equal(ApplyCalls, _cfg.Calls);
    }

    [Fact]
    public void Failed_apply_keeps_entry_and_feature_rollback_reverts_it()
    {
        var journal = NewJournal();
        _cfg.FailApply = true;

        Assert.Throws<ConfiguratorException>(() => new SystemChanges(journal, _cfg).AddDnsRoute("dns", SteamDomains, IPAddress.Loopback));
        Assert.Single(journal.Entries);

        Assert.True(journal.Rollback("dns").Success);
        Assert.Empty(journal.Entries);
        Assert.Contains("revert:dns:LunaDNS test", _cfg.Calls);
    }

    [Fact]
    public void Rollback_all_after_restart_reverts_lifo_and_flushes()
    {
        ApplyAll(new SystemChanges(NewJournal(), _cfg));
        _cfg.Calls.Clear();

        var restarted = NewJournal();
        var result = restarted.RollbackAll();

        Assert.True(result.Success);
        Assert.Equal(3, result.Reverted);
        Assert.Equal(RevertCalls, _cfg.Calls);
    }

    [Fact]
    public void Rollback_after_crash_between_record_and_apply_runs_revert_once()
    {
        _cfg.FailApply = true;
        Assert.Throws<ConfiguratorException>(() => new SystemChanges(NewJournal(), _cfg).SetAutoProxy("proxy", PacUrl));
        _cfg.FailApply = false;
        _cfg.Calls.Clear();

        var result = NewJournal().RollbackAll();

        Assert.Equal(1, result.Reverted);
        Assert.Equal(ProxyRevertCall, _cfg.Calls);
    }

    [Fact]
    public void Snapshot_with_wrong_shape_is_kept_as_failure()
    {
        var journal = NewJournal();
        journal.Record("dns", ChangeOps.DnsRoute, "không phải object");

        var result = journal.RollbackAll();

        var failure = Assert.Single(result.Failed);
        Assert.IsType<JsonException>(failure.Error);
        Assert.Single(journal.Entries);
    }
}
