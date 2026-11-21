using System.Net;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Nrpt;

namespace LunaDns.Platform.Windows.Tests.Nrpt;

public class NrptDnsRouterTests
{
    private static readonly string[] Steam = ["steampowered.com", "steamserver.net"];
    private static readonly string[] SteamApex = ["steampowered.com"];
    private static readonly string[] SteamNamespaces = [".steampowered.com", "steampowered.com", ".steamserver.net", "steamserver.net"];
    private static readonly string[] SkippedSuffix = [".steampowered.com"];

    private readonly FakeNrptStore _store = new();
    private readonly NrptRule _tailscale = FakeNrptStore.Tailscale();
    private readonly NrptDnsRouter _router;

    public NrptDnsRouterTests()
    {
        _store.Rules.Add(_tailscale);
        _router = new NrptDnsRouter(_store);
    }

    [Fact]
    public void Plan_routes_suffix_and_exact_name_to_stub()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        Assert.Equal(SteamNamespaces, snapshot.Namespaces);
        Assert.Equal("127.0.0.1", snapshot.NameServer);
        Assert.Empty(snapshot.SkippedNamespaces);
        Assert.StartsWith(NrptDnsRouter.MarkerPrefix, snapshot.Marker);
    }

    [Fact]
    public void Apply_then_revert_keeps_tailscale_rule_untouched()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        _router.Apply(snapshot);
        var ours = Assert.Single(_store.Rules, r => r.Comment == snapshot.Marker);
        Assert.Equal(NrptDnsRouter.DisplayName, ours.DisplayName);
        Assert.Equal(2, _store.Rules.Count);
        _router.Revert(snapshot);

        Assert.Same(_tailscale, Assert.Single(_store.Rules));
    }

    [Fact]
    public void Plan_skips_namespace_owned_by_foreign_rule_case_insensitively()
    {
        _store.Rules[0] = FakeNrptStore.Tailscale(".STEAMPOWERED.com");

        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        Assert.Equal(SkippedSuffix, snapshot.SkippedNamespaces);
        Assert.DoesNotContain(".steampowered.com", snapshot.Namespaces);
        Assert.Contains("steampowered.com", snapshot.Namespaces);
    }

    [Fact]
    public void Plan_throws_when_every_namespace_belongs_to_foreign_rule()
    {
        _store.Rules.Add(new NrptRule("{corp}", [".steampowered.com", "steampowered.com"], ["10.0.0.1"], "corp vpn", null));

        var error = Assert.Throws<ConfiguratorException>(() => _router.Plan(SteamApex, IPAddress.Loopback));

        Assert.Equal(ConfiguratorError.DnsRouteConflict, error.Error);
    }

    [Fact]
    public void Apply_twice_adds_one_rule()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        _router.Apply(snapshot);
        _router.Apply(snapshot);

        Assert.Equal(1, _store.AddCalls);
    }

    [Fact]
    public void Revert_before_apply_and_revert_twice_are_noops()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);

        _router.Revert(snapshot);
        _router.Apply(snapshot);
        _router.Revert(snapshot);
        _router.Revert(snapshot);

        Assert.Same(_tailscale, Assert.Single(_store.Rules));
    }

    [Fact]
    public void Revert_removes_only_rule_with_its_own_marker()
    {
        var first = _router.Plan(SteamApex, IPAddress.Loopback);
        var second = _router.Plan(Steam, IPAddress.Loopback);
        _router.Apply(first);
        _router.Apply(second);

        _router.Revert(first);

        Assert.Equal(2, _store.Rules.Count);
        Assert.Contains(_store.Rules, r => r.Comment == second.Marker);
    }

    [Fact]
    public void Snapshot_without_lunadns_marker_never_touches_rules()
    {
        _store.Rules.Add(new NrptRule("{empty-comment}", [".example.org"], ["10.0.0.1"], "", null));
        var bad = new DnsRouteSnapshot("", SteamNamespaces, "127.0.0.1", []);

        Assert.Throws<ArgumentException>(() => _router.Revert(bad));
        Assert.Throws<ArgumentException>(() => _router.Apply(bad));
        Assert.Equal(2, _store.Rules.Count);
    }

    [Fact]
    public void Plan_accepts_custom_routable_domains()
    {
        var snapshot = _router.Plan(["reddit.com", "example.org"], IPAddress.Loopback);

        Assert.Equal([".reddit.com", "reddit.com", ".example.org", "example.org"], snapshot.Namespaces);
    }

    [Theory]
    [InlineData("com")]
    [InlineData("com.vn")]
    [InlineData("printer.local")]
    [InlineData("dns.google")]
    [InlineData("../etc/passwd")]
    public void Plan_rejects_non_routable_domains(string domain)
    {
        string[] domains = [domain];

        Assert.Throws<ArgumentException>(() => _router.Plan(domains, IPAddress.Loopback));
    }

    [Fact]
    public void Plan_rejects_non_loopback_name_server()
    {
        Assert.Throws<ArgumentException>(() => _router.Plan(Steam, IPAddress.Parse("1.1.1.1")));
    }
}
