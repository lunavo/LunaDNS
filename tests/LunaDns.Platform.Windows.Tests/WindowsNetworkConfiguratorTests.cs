using System.Net;
using System.Text;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;
using LunaDns.Platform.Windows.Tests.Nrpt;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Tests;

public sealed class WindowsNetworkConfiguratorTests : IDisposable
{
    private static readonly string[] Steam = ["steampowered.com", "steamcommunity.com", "steamserver.net"];
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");
    private static readonly byte[] OriginalHosts = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# hosts thử\r\n127.0.0.1 localhost")];

    private readonly TempDirectory _dir = new();
    private readonly string _sid = AutoProxyRegistry.CurrentUserSid();
    private readonly string _proxyKey = $@"Software\LunaDNS.Tests\{Guid.NewGuid():N}";
    private readonly FakeNrptStore _nrpt = new();
    private readonly NrptRule _tailscale = FakeNrptStore.Tailscale();
    private int _flushes;

    public WindowsNetworkConfiguratorTests()
    {
        _nrpt.Rules.Add(_tailscale);
        File.WriteAllBytes(HostsPath, OriginalHosts);
        File.SetAttributes(HostsPath, FileAttributes.ReadOnly);
        Registry.Users.CreateSubKey($@"{_sid}\{_proxyKey}").Dispose();
    }

    private string HostsPath => _dir.PathOf("hosts");

    private string JournalPath => _dir.PathOf("journal.json");

    public void Dispose()
    {
        Registry.Users.DeleteSubKeyTree($@"{_sid}\{_proxyKey}", throwOnMissingSubKey: false);
        _dir.Dispose();
    }

    private WindowsNetworkConfigurator NewConfigurator() => new(
        new NrptDnsRouter(_nrpt),
        new AutoProxyRegistry(_proxyKey, () => true),
        new HostsFileStore(HostsPath),
        () =>
        {
            _flushes++;
            return true;
        });

    private ChangeJournal NewJournal(INetworkConfigurator cfg) =>
        new(new JsonFileJournalStore(JournalPath), SystemChanges.Reverters(cfg));

    [Fact]
    public void Three_changes_survive_restart_and_roll_back_to_original_state()
    {
        var cfg = NewConfigurator();
        var changes = new SystemChanges(NewJournal(cfg), cfg);
        changes.AddDnsRoute("dns-routing", Steam, IPAddress.Loopback);
        changes.SetAutoProxy("sni-proxy", PacUrl);
        changes.AddHostsEntries("pubg", Pubg);
        Assert.Equal(2, _nrpt.Rules.Count);
        Assert.Contains("# LunaDNS", Encoding.UTF8.GetString(File.ReadAllBytes(HostsPath)));

        // "Crash": tiến trình mới đọc lại journal từ file.
        var restarted = NewJournal(NewConfigurator());
        Assert.Equal(3, restarted.Entries.Count);
        var result = restarted.RollbackAll();

        Assert.True(result.Success);
        Assert.Equal(3, result.Reverted);
        Assert.Empty(restarted.Entries);
        Assert.Same(_tailscale, Assert.Single(_nrpt.Rules));
        Assert.Equal(OriginalHosts, File.ReadAllBytes(HostsPath));
        Assert.Equal(FileAttributes.ReadOnly, File.GetAttributes(HostsPath));
        using var key = Registry.Users.OpenSubKey($@"{_sid}\{_proxyKey}")!;
        Assert.Null(key.GetValue(AutoProxyRegistry.ValueName));
        Assert.Equal(4, _flushes); // apply dns, apply hosts, revert hosts, revert dns
    }

    [Fact]
    public void Failed_nrpt_add_leaves_entry_that_feature_rollback_clears()
    {
        _nrpt.FailAdd = true;
        var cfg = NewConfigurator();
        var journal = NewJournal(cfg);
        var changes = new SystemChanges(journal, cfg);

        var error = Assert.Throws<ConfiguratorException>(() => changes.AddDnsRoute("dns-routing", Steam, IPAddress.Loopback));

        Assert.Equal(ConfiguratorError.NrptFailed, error.Error);
        Assert.Single(journal.Entries);
        Assert.True(journal.Rollback("dns-routing").Success);
        Assert.Empty(journal.Entries);
        Assert.Same(_tailscale, Assert.Single(_nrpt.Rules));
    }
}
