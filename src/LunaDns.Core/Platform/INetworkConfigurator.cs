using System.Net;

namespace LunaDns.Core.Platform;

/// <summary>
/// Thao tác hệ thống chia ba bước để Journal ghi TRƯỚC khi đổi (spec 4.5):
/// Plan* chỉ đọc và trả snapshot, Apply* đổi hệ thống theo snapshot (chạy lại không nhân đôi),
/// Revert* hoàn tác theo snapshot (idempotent). Chỉ gọi qua <see cref="SystemChanges"/>.
/// </summary>
public interface INetworkConfigurator
{
    DnsRouteSnapshot PlanDnsRoute(IReadOnlyList<string> domains, IPAddress nameServer);
    void ApplyDnsRoute(DnsRouteSnapshot snapshot);
    void RevertDnsRoute(DnsRouteSnapshot snapshot);

    AutoProxySnapshot PlanAutoProxy(Uri pacUrl, bool allowOverride);
    void ApplyAutoProxy(AutoProxySnapshot snapshot);
    void RevertAutoProxy(AutoProxySnapshot snapshot);

    HostsSnapshot PlanHostsEntries(IReadOnlyList<HostsEntry> entries);
    void ApplyHostsEntries(HostsSnapshot snapshot);
    void RevertHostsEntries(HostsSnapshot snapshot);

    /// <summary>
    /// Gỡ dấu vết mang dấu LunaDNS không thuộc <paramref name="coverage"/> (spec F7). Chỉ <see cref="OrphanSweeper"/> gọi.
    /// Bản cài đặt dùng <see cref="OrphanSweeper.RunParts"/>; không xả cache DNS (OrphanSweeper lo).
    /// </summary>
    OrphanSweepResult SweepOrphans(JournalCoverage coverage);

    /// <summary>Xoá cache DNS của hệ điều hành. Không ném lỗi; lỗi thì bản cài đặt tự ghi log.</summary>
    void FlushDnsCache();
}
