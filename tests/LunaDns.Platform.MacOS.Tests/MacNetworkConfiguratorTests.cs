using LunaDns.Core.Domains;
using System.Net;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Platform.MacOS.Hosts;
using LunaDns.Platform.MacOS.Proxy;
using LunaDns.Platform.MacOS.Resolver;

namespace LunaDns.Platform.MacOS.Tests;

/// <summary>Đi trọn đường Core: SystemChanges ghi journal ra đĩa, "crash", journal mới hoàn tác hết. Không đụng hệ thống thật.</summary>
public sealed class MacNetworkConfiguratorTests : IDisposable
{
    private const string OriginalHosts = "##\n# Host Database\n127.0.0.1\tlocalhost\n";
    private readonly TempDirectory _tmp = new();
    private readonly FakeNetworkSetup _ns = new();
    private readonly MacNetworkConfigurator _cfg;

    public MacNetworkConfiguratorTests()
    {
        File.WriteAllText(_tmp.Combine("hosts"), OriginalHosts);
        _cfg = new MacNetworkConfigurator(
            new ResolverDnsRouter(15353, _tmp.Combine("resolver")),
            new NetworkSetupAutoProxy(_ns),
            new UnixHostsFileStore(_tmp.Combine("hosts")),
            _ns);
    }

    public void Dispose() => _tmp.Dispose();

    private ChangeJournal NewJournal() =>
        new(new JsonFileJournalStore(_tmp.Combine("journal.json")), SystemChanges.Reverters(_cfg));

    [Fact]
    public void Everything_is_undone_after_a_simulated_crash()
    {
        var journal = NewJournal();
        var changes = new SystemChanges(journal, _cfg);
        changes.AddDnsRoute("dns", FeatureDomains.Routed(new DomainSelection()), IPAddress.Loopback);
        changes.SetAutoProxy("proxy", new Uri("http://127.0.0.1:18080/proxy.pac"));
        changes.AddHostsEntries("pubg", FeatureDomains.PubgHosts);

        Assert.Equal(FeatureDomains.Routed(new DomainSelection()).Count, _cfg.Dns.ListOurs().Count);
        Assert.Contains("# LunaDNS", File.ReadAllText(_tmp.Combine("hosts")), StringComparison.Ordinal);
        Assert.Contains(_ns.Calls, c => c.Contains("killall -HUP mDNSResponder", StringComparison.Ordinal));

        var result = NewJournal().RollbackAll();

        Assert.True(result.Success);
        Assert.Equal(3, result.Reverted);
        Assert.Empty(_cfg.Dns.ListOurs());
        Assert.Equal(OriginalHosts, File.ReadAllText(_tmp.Combine("hosts")));
        Assert.Equal(("http://127.0.0.1:8099/canopi.pac", false), _ns.Auto["Luna Wifi"]);
    }

    [Fact]
    public void Corrupt_journal_leaves_nothing_behind_after_startup_sweep()
    {
        var changes = new SystemChanges(NewJournal(), _cfg);
        changes.AddDnsRoute("dns", FeatureDomains.Routed(new DomainSelection()), IPAddress.Loopback);
        changes.SetAutoProxy("proxy", new Uri("http://127.0.0.1:18080/proxy.pac"));
        changes.AddHostsEntries("pubg", FeatureDomains.PubgHosts);
        File.WriteAllText(_tmp.Combine("journal.json"), "[{\"Id\": \"dở"); // kill -9 lúc đang ghi

        var journal = NewJournal();
        Assert.True(journal.RollbackAll().Success);
        var result = OrphanSweeper.Sweep(journal, _cfg, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.True(result.Success);
        Assert.Empty(_cfg.Dns.ListOurs());
        Assert.Empty(_cfg.Proxy.ServicesUsingLunaDnsPac());
        Assert.Equal(OriginalHosts, File.ReadAllText(_tmp.Combine("hosts")));
        Assert.Single(Directory.GetFiles(_tmp.Path, "journal.json.corrupt-*"));
    }

    [Fact]
    public void Sweep_failure_of_one_kind_does_not_stop_the_others()
    {
        var changes = new SystemChanges(NewJournal(), _cfg);
        changes.AddDnsRoute("dns", FeatureDomains.Routed(new DomainSelection()), IPAddress.Loopback);
        changes.AddHostsEntries("pubg", FeatureDomains.PubgHosts);
        File.Delete(_tmp.Combine("journal.json"));
        _ns.FailList = true;

        var result = _cfg.SweepOrphans(JournalCoverage.From([]));

        Assert.Equal(ChangeOps.AutoProxy, Assert.Single(result.Failed).Op);
        Assert.Empty(_cfg.Dns.ListOurs());
        Assert.Equal(OriginalHosts, File.ReadAllText(_tmp.Combine("hosts")));
    }
}
