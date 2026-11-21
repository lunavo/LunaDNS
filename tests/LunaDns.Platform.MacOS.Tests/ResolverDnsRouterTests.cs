using System.Net;
using LunaDns.Core.Platform;
using LunaDns.Platform.MacOS.Resolver;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class ResolverDnsRouterTests : IDisposable
{
    private static readonly string[] Steam = ["steampowered.com", "SteamCommunity.com", "steampowered.com"];
    private readonly TempDirectory _tmp = new();
    private readonly ResolverDnsRouter _router;

    public ResolverDnsRouterTests() => _router = new ResolverDnsRouter(15353, _tmp.Combine("resolver"));

    public void Dispose() => _tmp.Dispose();

    private string FileFor(string host) => Path.Combine(_router.Directory, host);

    [Fact]
    public void Plan_accepts_custom_routable_domains()
    {
        var snapshot = _router.Plan(["reddit.com", "example.org"], IPAddress.Loopback);

        Assert.Equal(["reddit.com", "example.org"], snapshot.Namespaces);
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
    public void Apply_writes_one_file_per_domain_with_marker_and_port()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);
        _router.Apply(snapshot);

        Assert.Equal(["steampowered.com", "steamcommunity.com"], snapshot.Namespaces);
        Assert.Equal($"# {snapshot.Marker}\nnameserver 127.0.0.1\nport 15353\n", File.ReadAllText(FileFor("steampowered.com")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            File.GetUnixFileMode(FileFor("steamcommunity.com")));
        Assert.Equal(["steamcommunity.com", "steampowered.com"], _router.ListOurs());
    }

    [Fact]
    public void Apply_twice_and_revert_twice_are_idempotent()
    {
        var snapshot = _router.Plan(Steam, IPAddress.Loopback);
        _router.Apply(snapshot);
        _router.Apply(snapshot);
        _router.Revert(snapshot);
        _router.Revert(snapshot);

        Assert.Empty(Directory.EnumerateFiles(_router.Directory));
    }

    [Fact]
    public void Foreign_file_is_skipped_and_never_touched()
    {
        Directory.CreateDirectory(_router.Directory);
        File.WriteAllText(FileFor("steampowered.com"), "nameserver 1.1.1.1\n");

        var snapshot = _router.Plan(Steam, IPAddress.Loopback);
        _router.Apply(snapshot);
        _router.Revert(snapshot);

        Assert.Equal(["steamcommunity.com"], snapshot.Namespaces);
        Assert.Equal(["steampowered.com"], snapshot.SkippedNamespaces);
        Assert.Equal("nameserver 1.1.1.1\n", File.ReadAllText(FileFor("steampowered.com")));
        Assert.False(File.Exists(FileFor("steamcommunity.com")));
    }

    [Fact]
    public void All_domains_taken_by_others_is_a_conflict()
    {
        Directory.CreateDirectory(_router.Directory);
        File.WriteAllText(FileFor("steampowered.com"), "nameserver 1.1.1.1\n");

        var ex = Assert.Throws<ConfiguratorException>(() => _router.Plan(["steampowered.com"], IPAddress.Loopback));
        Assert.Equal(ConfiguratorError.DnsRouteConflict, ex.Error);
    }

    [Fact]
    public void Revert_only_removes_files_with_the_same_marker()
    {
        var first = _router.Plan(["steampowered.com"], IPAddress.Loopback);
        _router.Apply(first);
        var stale = first with { Marker = ResolverDnsRouter.MarkerPrefix + "other" };

        _router.Revert(stale);

        Assert.True(File.Exists(FileFor("steampowered.com")));
    }

    [Fact]
    public void Leftover_lunadns_file_from_crash_is_taken_over()
    {
        var old = _router.Plan(["steampowered.com"], IPAddress.Loopback);
        _router.Apply(old);

        var fresh = _router.Plan(["steampowered.com"], IPAddress.Loopback);
        _router.Apply(fresh);
        _router.Revert(fresh);

        Assert.Empty(fresh.SkippedNamespaces);
        Assert.False(File.Exists(FileFor("steampowered.com")));
    }

    [Fact]
    public void Rejects_non_routable_domain_and_non_loopback_server()
    {
        Assert.Throws<ArgumentException>(() => _router.Plan(["printer.local"], IPAddress.Loopback));
        Assert.Throws<ArgumentException>(() => _router.Plan(["steampowered.com"], IPAddress.Parse("1.1.1.1")));
    }

    [Theory]
    [InlineData("../hosts")]
    [InlineData("..steampowered.com")]
    [InlineData("printer.local")]
    public void Snapshot_from_tampered_journal_is_refused(string name)
    {
        var bad = new DnsRouteSnapshot(ResolverDnsRouter.MarkerPrefix + "x", [name], "127.0.0.1", []);

        Assert.Throws<ArgumentException>(() => _router.Revert(bad));
        Assert.Throws<ArgumentException>(() => _router.Apply(bad));
    }

    [Fact]
    public void Snapshot_without_marker_is_refused()
    {
        var bad = new DnsRouteSnapshot("", ["steampowered.com"], "127.0.0.1", []);
        Assert.Throws<ArgumentException>(() => _router.Revert(bad));
    }
}
