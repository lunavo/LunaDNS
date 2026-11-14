using System.Net;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Tests.Platform;

/// <summary>Ghi lại thứ tự gọi. BeforeApply chạy ngay trước mỗi Apply, để test kiểm tra journal đã ghi chưa.</summary>
internal sealed class FakeConfigurator : INetworkConfigurator
{
    public List<string> Calls { get; } = [];
    public Action? BeforeApply { get; set; }
    public bool FailApply { get; set; }

    public bool FailRevert { get; set; }

    /// <summary>Giả lập máy có sẵn PAC/proxy thủ công: PlanAutoProxy ném ExistingProxyConfig khi chưa allowOverride.</summary>
    public bool ExistingProxy { get; set; }

    public ConfiguratorError? PlanDnsError { get; set; }

    public DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer)
    {
        Calls.Add("plan:dns");
        if (PlanDnsError is { } error) throw new ConfiguratorException(error, "giả lập lỗi plan");
        return new DnsRouteSnapshot("LunaDNS test", [.. domains.Select(d => "." + d)], nameServer.ToString(), []);
    }

    public void ApplyDnsRoute(DnsRouteSnapshot snapshot) => Apply("apply:dns");

    public void RevertDnsRoute(DnsRouteSnapshot snapshot) => Revert($"revert:dns:{snapshot.Marker}");

    public AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride)
    {
        Calls.Add("plan:proxy");
        if (ExistingProxy && !allowOverride)
            throw new ConfiguratorException(ConfiguratorError.ExistingProxyConfig, "giả lập proxy có sẵn");
        return new AutoProxySnapshot("S-1-5-21-1", false, null, false, pacUrl.AbsoluteUri);
    }

    public void ApplyAutoProxy(AutoProxySnapshot snapshot) => Apply("apply:proxy");

    public void RevertAutoProxy(AutoProxySnapshot snapshot) => Revert($"revert:proxy:{snapshot.NewValue}");

    public HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries)
    {
        Calls.Add("plan:hosts");
        return new HostsSnapshot([.. entries.Select(e => $"{e.Address} {e.Host}")], "\r\n", false);
    }

    public void ApplyHostsEntries(HostsSnapshot snapshot) => Apply("apply:hosts");

    public void RevertHostsEntries(HostsSnapshot snapshot) => Revert($"revert:hosts:{snapshot.Lines[0]}");

    public void FlushDnsCache() => Calls.Add("flush");

    private void Revert(string call)
    {
        Calls.Add(call);
        if (FailRevert) throw new ConfiguratorException(ConfiguratorError.AccessDenied, "giả lập lỗi hoàn tác");
    }

    private void Apply(string call)
    {
        BeforeApply?.Invoke();
        Calls.Add(call);
        if (FailApply) throw new ConfiguratorException(ConfiguratorError.Other, "giả lập lỗi");
    }
}
