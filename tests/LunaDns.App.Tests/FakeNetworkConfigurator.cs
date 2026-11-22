using System.Net;
using LunaDns.Core.Hosts;
using LunaDns.Core.Platform;

namespace LunaDns.App.Tests;

/// <summary>Configurator giả: Plan trả snapshot dựng sẵn, Apply không làm gì, Revert đếm số lần (hoặc ném khi FailRevert).</summary>
internal sealed class FakeNetworkConfigurator : INetworkConfigurator
{
    public bool FailRevert { get; set; }
    public int Reverts { get; private set; }

    public DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer) =>
        new("LunaDNS test", [.. domains], nameServer.ToString(), []);

    public void ApplyDnsRoute(DnsRouteSnapshot snapshot) { }

    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => Revert();

    public AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride) =>
        new("S-1-5-21-1-2-3-1001", false, null, false, pacUrl.AbsoluteUri);

    public void ApplyAutoProxy(AutoProxySnapshot snapshot) { }

    public void RevertAutoProxy(AutoProxySnapshot snapshot) => Revert();

    public HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries) =>
        new([.. entries.Select(e => $"{e.Address} {e.Host} # LunaDNS")], "\r\n", false);

    public void ApplyHostsEntries(HostsSnapshot snapshot) { }

    public void RevertHostsEntries(HostsSnapshot snapshot) => Revert();

    public void FlushDnsCache() { }

    /// <summary>Số lần quét dọn, và số Reverts lúc quét (để test kiểm quét chạy sau hoàn tác).</summary>
    public int Sweeps { get; private set; }

    public int RevertsAtSweep { get; private set; }

    public bool FailSweep { get; set; }

    public OrphanSweepResult SweepOrphans(JournalCoverage coverage)
    {
        Sweeps++;
        RevertsAtSweep = Reverts;
        return FailSweep ? new OrphanSweepResult(0, 0, 0, [new OrphanSweepFailure(ChangeOps.DnsRoute, new IOException("giả lập"))])
            : OrphanSweepResult.Empty;
    }

    private void Revert()
    {
        if (FailRevert) throw new ConfiguratorException(ConfiguratorError.AccessDenied, "giả lập lỗi hoàn tác");
        Reverts++;
    }
}
