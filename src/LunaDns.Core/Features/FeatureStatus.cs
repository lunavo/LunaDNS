using LunaDns.Core.Probing;

namespace LunaDns.Core.Features;

/// <summary>Khu vực trạng thái (spec 4.8): đang bật gì, cổng proxy, số kết nối.</summary>
public sealed record FeatureStatus(bool DnsRouting, bool SniProxy, bool PubgMode, int? ProxyPort, int ProxyConnections)
{
    /// <summary>Biện pháp của bảng spec 4.1 đang bật. Chế độ PUBG không thuộc bảng này.</summary>
    public ProbeRemedies Remedies =>
        (DnsRouting ? ProbeRemedies.DnsRouting : ProbeRemedies.None) | (SniProxy ? ProbeRemedies.SniProxy : ProbeRemedies.None);
}
