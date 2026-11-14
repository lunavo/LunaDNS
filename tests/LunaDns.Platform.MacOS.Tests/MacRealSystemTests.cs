using System.Net;
using LunaDns.Core.Features;
using LunaDns.Platform.MacOS.Processes;
using LunaDns.Platform.MacOS.Proxy;

namespace LunaDns.Platform.MacOS.Tests;

/// <summary>
/// Chạy tay trên máy Mac thật, bằng root:
/// <c>sudo LUNADNS_ADMIN_TESTS=1 dotnet test tests/LunaDns.Platform.MacOS.Tests --filter Category=MacAdmin</c>.
/// Mỗi test áp dụng rồi hoàn tác, và so sánh trạng thái trước/sau.
/// </summary>
[Collection(RealSystemCollection.Name)]
[Trait("Category", "MacAdmin")]
public class MacRealSystemTests
{
    // steampowered.com/steamcommunity.com có thể đã có file tay (steam-dns); dùng domain ít ai đụng.
    private static readonly string[] Domains = ["steamstatic.com", "steam-chat.com"];
    private readonly MacNetworkConfigurator _cfg = MacNetworkConfigurator.CreateDefault();
    private readonly ProcessRunner _runner = new();

    private static void RequireRoot() =>
        Assert.True(Environment.UserName == "root", "Test này phải chạy bằng sudo.");

    [MacAdminFact]
    public void Resolver_files_reach_mdnsresponder_and_are_removed()
    {
        RequireRoot();
        var before = ListResolver();

        var snapshot = _cfg.PlanDnsRoute(Domains, IPAddress.Loopback);
        try
        {
            _cfg.ApplyDnsRoute(snapshot);
            _cfg.FlushDnsCache();
            Thread.Sleep(1000);
            var dns = _runner.Run("/usr/sbin/scutil", ["--dns"]).Output;
            Assert.Contains("domain   : steamstatic.com", dns, StringComparison.Ordinal);
            Assert.Contains("port     : 15353", dns, StringComparison.Ordinal);
        }
        finally
        {
            _cfg.RevertDnsRoute(snapshot);
            _cfg.FlushDnsCache();
        }

        Assert.Equal(before, ListResolver());
    }

    [MacAdminFact]
    public void Hosts_is_byte_identical_after_apply_and_revert()
    {
        RequireRoot();
        var before = _cfg.Hosts.Read();

        var snapshot = _cfg.PlanHostsEntries(FeatureDomains.PubgHosts);
        try
        {
            _cfg.ApplyHostsEntries(snapshot);
            Assert.Contains("# LunaDNS", System.Text.Encoding.UTF8.GetString(_cfg.Hosts.Read()), StringComparison.Ordinal);
        }
        finally
        {
            _cfg.RevertHostsEntries(snapshot);
        }

        Assert.Equal(before, _cfg.Hosts.Read());
        Assert.NotNull(new DirectoryInfo("/etc").LinkTarget); // vẫn là symlink tới /private/etc
    }

    [MacAdminFact]
    public void Auto_proxy_state_is_restored_on_every_service()
    {
        RequireRoot();
        var services = _cfg.Proxy.ListServices();
        var before = services.ToDictionary(s => s, Get);

        var snapshot = _cfg.PlanAutoProxy(new Uri("http://127.0.0.1:18080/proxy.pac"), allowOverride: true);
        try
        {
            _cfg.ApplyAutoProxy(snapshot);
            Assert.All(services, s => Assert.Equal(("http://127.0.0.1:18080/proxy.pac", true), Get(s)));
        }
        finally
        {
            _cfg.RevertAutoProxy(snapshot);
        }

        foreach (var service in services)
        {
            var (url, enabled) = Get(service);
            Assert.Equal(before[service].Enabled, enabled);
            if (before[service].Url is not null) Assert.Equal(before[service].Url, url);
        }
    }

    private (string? Url, bool Enabled) Get(string service) =>
        NetworkSetupAutoProxy.ParseAutoProxy(_runner.Run(NetworkSetupAutoProxy.Tool, ["-getautoproxyurl", service]).Output);

    private static string[] ListResolver() =>
        Directory.Exists("/etc/resolver")
            ? [.. Directory.EnumerateFiles("/etc/resolver").Select(f => f + "\n" + File.ReadAllText(f)).Order(StringComparer.Ordinal)]
            : [];
}
