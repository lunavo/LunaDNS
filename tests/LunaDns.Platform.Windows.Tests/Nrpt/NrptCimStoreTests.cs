using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.Platform.Windows.Tests.Nrpt;

[Collection(RealSystemCollection.Name)]
public class NrptCimStoreTests
{
    private static readonly string[] TestNamespace = [".lunadns-admin-test.invalid"];
    private static readonly string[] LoopbackServer = ["127.0.0.1"];
    private static readonly string[] SteamApex = ["steampowered.com"];
    private readonly NrptCimStore _store = new();

    private static string Describe(NrptRule r) =>
        $"{r.Name}|{string.Join(',', r.Namespaces)}|{string.Join(',', r.NameServers)}|{r.Comment}|{r.DisplayName}";

    private string[] AllRules() => [.. _store.List().Select(Describe).Order(StringComparer.Ordinal)];

    [WindowsFact]
    public void List_works_without_admin()
    {
        foreach (var rule in _store.List()) Assert.False(string.IsNullOrEmpty(rule.Name));
    }

    [AdminFact]
    public void Add_and_remove_real_rule_leave_other_rules_untouched()
    {
        var before = AllRules();
        var comment = NrptDnsRouter.MarkerPrefix + "admin-test-" + Guid.NewGuid().ToString("N");
        try
        {
            _store.Add(TestNamespace, "127.0.0.1", comment, NrptDnsRouter.DisplayName);
            var added = Assert.Single(_store.List(), r => r.Comment == comment);
            Assert.Equal(TestNamespace, added.Namespaces);
            Assert.Equal(LoopbackServer, added.NameServers);
            Assert.Equal(NrptDnsRouter.DisplayName, added.DisplayName);
            _store.Remove(added.Name);
        }
        finally
        {
            foreach (var leftover in _store.List().Where(r => r.Comment == comment)) _store.Remove(leftover.Name);
        }

        Assert.Equal(before, AllRules());
    }

    /// <summary>Mạng PO: DNS hệ thống trả 127.0.0.1 cho store.steampowered.com. Có NRPT → stub → DoH thì phải ra IP thật.</summary>
    [AdminNetworkFact]
    public async Task Steam_resolves_through_stub_while_routed_and_rules_restore_after()
    {
        var before = AllRules();
        using var doh = DohClient.Create(DohServer.Defaults);
        var handler = new DnsQueryHandler(DomainCatalog.All, doh, new DnsCache());
        await using var stub = DohStubServer.Start(DohStubServer.DefaultEndPoint, handler);
        var router = new NrptDnsRouter(_store);
        var snapshot = router.Plan(SteamApex, IPAddress.Loopback);
        try
        {
            router.Apply(snapshot);
            Assert.True(WindowsDns.FlushResolverCache());

            var addresses = await System.Net.Dns.GetHostAddressesAsync("store.steampowered.com", AddressFamily.InterNetwork);

            Assert.NotEmpty(addresses);
            Assert.DoesNotContain(addresses, KnownBlockIps.IsBlockAddress);
        }
        finally
        {
            router.Revert(snapshot);
            WindowsDns.FlushResolverCache();
        }

        Assert.Equal(before, AllRules());
    }
}
