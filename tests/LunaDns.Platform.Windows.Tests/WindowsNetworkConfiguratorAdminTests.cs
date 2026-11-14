using System.Net;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;
using Microsoft.Win32;

namespace LunaDns.Platform.Windows.Tests;

/// <summary>
/// Vòng áp dụng/hoàn tác trên hệ thống THẬT (NRPT, file hosts, AutoConfigURL của người đang chạy). Spec 9: hosts phải giống
/// hệt byte ban đầu, luật NRPT lạ (Tailscale) giữ nguyên. Có bản sao hosts ở %TEMP% để tự khôi phục nếu test sai.
/// </summary>
[Collection(RealSystemCollection.Name)]
public class WindowsNetworkConfiguratorAdminTests
{
    private static readonly string[] SteamApex = ["steampowered.com"];
    private static readonly HostsEntry[] Pubg = [new("127.0.0.1", "prod-live-front.playbattlegrounds.com")];
    private static readonly Uri PacUrl = new("http://127.0.0.1:18080/proxy.pac");

    private static string[] AllRules(NrptCimStore store) =>
        [.. store.List().Select(r => $"{r.Name}|{string.Join(',', r.Namespaces)}|{string.Join(',', r.NameServers)}|{r.Comment}").Order(StringComparer.Ordinal)];

    private static object? AutoConfigUrl()
    {
        using var key = Registry.CurrentUser.OpenSubKey(AutoProxyRegistry.InternetSettingsSubKey);
        return key?.GetValue(AutoProxyRegistry.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    [AdminFact]
    public void Real_system_round_trip_restores_hosts_bytes_proxy_and_nrpt()
    {
        var hostsPath = HostsFileStore.DefaultPath();
        var hostsBefore = File.ReadAllBytes(hostsPath);
        var attributesBefore = File.GetAttributes(hostsPath);
        var backup = Path.Combine(Path.GetTempPath(), $"lunadns-hosts-backup-{Guid.NewGuid():N}");
        File.Copy(hostsPath, backup);
        var nrpt = new NrptCimStore();
        var rulesBefore = AllRules(nrpt);
        var proxyBefore = AutoConfigUrl();
        var journalPath = Path.Combine(Path.GetTempPath(), $"lunadns-journal-{Guid.NewGuid():N}.json");

        var cfg = WindowsNetworkConfigurator.CreateDefault();
        var journal = new ChangeJournal(new JsonFileJournalStore(journalPath), SystemChanges.Reverters(cfg));
        var changes = new SystemChanges(journal, cfg);
        RollbackResult result;
        try
        {
            changes.AddDnsRoute("dns-routing", SteamApex, IPAddress.Loopback);
            changes.SetAutoProxy("sni-proxy", PacUrl);
            changes.AddHostsEntries("pubg", Pubg);
            Assert.Equal(rulesBefore.Length + 1, AllRules(nrpt).Length);
        }
        finally
        {
            result = journal.RollbackAll();
        }

        var hostsAfter = File.ReadAllBytes(hostsPath);
        if (!hostsAfter.AsSpan().SequenceEqual(hostsBefore))
        {
            File.SetAttributes(hostsPath, FileAttributes.Normal);
            File.Copy(backup, hostsPath, overwrite: true);
            File.SetAttributes(hostsPath, attributesBefore);
        }
        else
        {
            File.Delete(backup);
        }
        File.Delete(journalPath);

        Assert.True(result.Success);
        Assert.Equal(hostsBefore, hostsAfter);
        Assert.Equal(attributesBefore, File.GetAttributes(hostsPath));
        Assert.Equal(rulesBefore, AllRules(nrpt));
        Assert.Equal(proxyBefore, AutoConfigUrl());
    }
}
