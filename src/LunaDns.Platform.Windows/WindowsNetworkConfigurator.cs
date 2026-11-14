using System.Net;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Platform.Windows;

/// <summary>Bản Windows của INetworkConfigurator: NRPT qua CIM, AutoConfigURL qua HKU\&lt;SID&gt;, file hosts ghi nguyên tử.</summary>
public sealed partial class WindowsNetworkConfigurator : INetworkConfigurator
{
    private readonly NrptDnsRouter _dns;
    private readonly AutoProxyRegistry _proxy;
    private readonly HostsFileStore _hosts;
    private readonly Func<bool> _flush;
    private readonly ILogger _logger;

    public WindowsNetworkConfigurator(
        NrptDnsRouter dns,
        AutoProxyRegistry proxy,
        HostsFileStore hosts,
        Func<bool>? flushDnsCache = null,
        ILogger<WindowsNetworkConfigurator>? logger = null)
    {
        _dns = dns;
        _proxy = proxy;
        _hosts = hosts;
        _flush = flushDnsCache ?? WindowsDns.FlushResolverCache;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    public static WindowsNetworkConfigurator CreateDefault(ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        return new WindowsNetworkConfigurator(
            new NrptDnsRouter(new NrptCimStore()),
            new AutoProxyRegistry(logger: factory.CreateLogger<AutoProxyRegistry>()),
            new HostsFileStore(HostsFileStore.DefaultPath()),
            logger: factory.CreateLogger<WindowsNetworkConfigurator>());
    }

    public DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer) => _dns.Plan(domains, nameServer);

    public void ApplyDnsRoute(DnsRouteSnapshot snapshot) => _dns.Apply(snapshot);

    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => _dns.Revert(snapshot);

    public AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride) => _proxy.Plan(pacUrl, allowOverride);

    public void ApplyAutoProxy(AutoProxySnapshot snapshot) => _proxy.Apply(snapshot);

    public void RevertAutoProxy(AutoProxySnapshot snapshot) => _proxy.Revert(snapshot);

    public HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries) => HostsEditor.Plan(_hosts.Read(), entries);

    public void ApplyHostsEntries(HostsSnapshot snapshot) => RewriteHosts(content => HostsEditor.Apply(content, snapshot));

    public void RevertHostsEntries(HostsSnapshot snapshot) => RewriteHosts(content => HostsEditor.Revert(content, snapshot));

    public void FlushDnsCache()
    {
        if (!_flush()) LogFlushFailed(_logger);
    }

    /// <summary>Chỉ ghi khi nội dung thật sự đổi, để Revert lần hai không đụng tới file.</summary>
    private void RewriteHosts(Func<byte[], byte[]> edit)
    {
        var current = _hosts.Read();
        var updated = edit(current);
        if (!current.AsSpan().SequenceEqual(updated)) _hosts.Write(updated);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không xoá được bộ nhớ đệm DNS của Windows; tên miền cũ có thể còn tới khi hết TTL.")]
    private static partial void LogFlushFailed(ILogger logger);
}
