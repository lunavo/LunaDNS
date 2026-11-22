using System.Net;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;
using LunaDns.Platform.MacOS.Hosts;
using LunaDns.Platform.MacOS.Processes;
using LunaDns.Platform.MacOS.Proxy;
using LunaDns.Platform.MacOS.Resolver;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Platform.MacOS;

/// <summary>Bản macOS của INetworkConfigurator: /etc/resolver, networksetup, /etc/hosts, xả cache mDNSResponder. Cần chạy bằng root.</summary>
public sealed partial class MacNetworkConfigurator : INetworkConfigurator
{
    /// <summary>Cổng stub DoH trên macOS. Tránh 53 (dễ đụng dnsmasq, Docker) và 5353 (mDNS).</summary>
    public const int DefaultDnsPort = 15353;

    private readonly IProcessRunner _runner;
    private readonly ILogger _logger;

    public MacNetworkConfigurator(
        ResolverDnsRouter dns,
        NetworkSetupAutoProxy proxy,
        UnixHostsFileStore hosts,
        IProcessRunner runner,
        ILogger<MacNetworkConfigurator>? logger = null)
    {
        Dns = dns;
        Proxy = proxy;
        Hosts = hosts;
        _runner = runner;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public ResolverDnsRouter Dns { get; }

    public NetworkSetupAutoProxy Proxy { get; }

    public UnixHostsFileStore Hosts { get; }

    public static MacNetworkConfigurator CreateDefault(int dnsPort = DefaultDnsPort, ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        var runner = new ProcessRunner();
        return new MacNetworkConfigurator(
            new ResolverDnsRouter(dnsPort),
            new NetworkSetupAutoProxy(runner, factory.CreateLogger<NetworkSetupAutoProxy>()),
            new UnixHostsFileStore(),
            runner,
            factory.CreateLogger<MacNetworkConfigurator>());
    }

    public DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer) => Dns.Plan(domains, nameServer);

    public void ApplyDnsRoute(DnsRouteSnapshot snapshot) => Dns.Apply(snapshot);

    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => Dns.Revert(snapshot);

    public AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride) => Proxy.Plan(pacUrl, allowOverride);

    public void ApplyAutoProxy(AutoProxySnapshot snapshot) => Proxy.Apply(snapshot);

    public void RevertAutoProxy(AutoProxySnapshot snapshot) => Proxy.Revert(snapshot);

    public HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries) => HostsEditor.Plan(Hosts.Read(), entries);

    public void ApplyHostsEntries(HostsSnapshot snapshot) => RewriteHosts(content => HostsEditor.Apply(content, snapshot));

    public void RevertHostsEntries(HostsSnapshot snapshot) => RewriteHosts(content => HostsEditor.Revert(content, snapshot));

    public OrphanSweepResult SweepOrphans(JournalCoverage coverage) =>
        OrphanSweeper.RunParts(coverage, Dns.SweepOrphans, Proxy.SweepOrphans, SweepHostsOrphans);

    private int SweepHostsOrphans(IReadOnlySet<string> coveredLines)
    {
        var removed = 0;
        RewriteHosts(content => HostsEditor.RemoveOrphans(content, coveredLines, out removed));
        return removed;
    }

    /// <summary>dscacheutil xoá cache thư viện, HUP cho mDNSResponder xoá cache của chính nó (cả /etc/resolver mới đọc lại).</summary>
    public void FlushDnsCache()
    {
        var ok = TryRun("/usr/bin/dscacheutil", "-flushcache");
        ok &= TryRun("/usr/bin/killall", "-HUP", "mDNSResponder");
        if (!ok) LogFlushFailed(_logger);
    }

    private bool TryRun(string tool, params string[] arguments)
    {
        try
        {
            return _runner.Run(tool, arguments).ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            LogToolFailed(_logger, tool, ex);
            return false;
        }
    }

    private void RewriteHosts(Func<byte[], byte[]> edit)
    {
        var current = Hosts.Read();
        var updated = edit(current);
        if (!current.AsSpan().SequenceEqual(updated)) Hosts.Write(updated);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không xoá được bộ nhớ đệm DNS của macOS; tên miền cũ có thể còn tới khi hết TTL.")]
    private static partial void LogFlushFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không chạy được {Tool}.")]
    private static partial void LogToolFailed(ILogger logger, string tool, Exception ex);
}
