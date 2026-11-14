using System.Net;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Platform;

/// <summary>
/// Cổng duy nhất để đổi hệ thống: Plan (chỉ đọc) → ChangeJournal.Record → Apply (spec 4.5, hợp đồng KH1).
/// Nếu Apply lỗi thì mục journal vẫn còn; người gọi (FeatureSet, KH5) gọi journal.Rollback(feature).
/// </summary>
public sealed class SystemChanges(ChangeJournal journal, INetworkConfigurator configurator)
{
    public DnsRouteSnapshot AddDnsRoute(string feature, IReadOnlyList<string> domains, IPAddress nameServer)
    {
        var snapshot = configurator.PlanDnsRoute(domains, nameServer);
        journal.Record(feature, ChangeOps.DnsRoute, snapshot);
        configurator.ApplyDnsRoute(snapshot);
        configurator.FlushDnsCache();
        return snapshot;
    }

    public AutoProxySnapshot SetAutoProxy(string feature, Uri pacUrl, bool allowOverride = false)
    {
        var snapshot = configurator.PlanAutoProxy(pacUrl, allowOverride);
        journal.Record(feature, ChangeOps.AutoProxy, snapshot);
        configurator.ApplyAutoProxy(snapshot);
        return snapshot;
    }

    public HostsSnapshot AddHostsEntries(string feature, IReadOnlyList<HostsEntry> entries)
    {
        var snapshot = configurator.PlanHostsEntries(entries);
        journal.Record(feature, ChangeOps.HostsEntries, snapshot);
        configurator.ApplyHostsEntries(snapshot);
        configurator.FlushDnsCache();
        return snapshot;
    }

    /// <summary>Reverter cho ChangeJournal. Dựng journal bằng các reverter này trước, rồi mới tạo SystemChanges.</summary>
    public static IReadOnlyList<IChangeReverter> Reverters(INetworkConfigurator configurator)
    {
        ArgumentNullException.ThrowIfNull(configurator);
        return
        [
            new SnapshotReverter<DnsRouteSnapshot>(ChangeOps.DnsRoute, s =>
            {
                configurator.RevertDnsRoute(s);
                configurator.FlushDnsCache();
            }),
            new SnapshotReverter<AutoProxySnapshot>(ChangeOps.AutoProxy, configurator.RevertAutoProxy),
            new SnapshotReverter<HostsSnapshot>(ChangeOps.HostsEntries, s =>
            {
                configurator.RevertHostsEntries(s);
                configurator.FlushDnsCache();
            }),
        ];
    }
}
