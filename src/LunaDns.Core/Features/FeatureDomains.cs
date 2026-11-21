using LunaDns.Core.Domains;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Features;

/// <summary>Domain mà mỗi tính năng tác động (KH5 quyết định 1 và 3).</summary>
public static class FeatureDomains
{
    /// <summary>Định tuyến DNS (stub + NRPT) và Proxy SNI (PAC, CONNECT): chỉ nhóm Steam.</summary>
    public static IReadOnlyList<string> Routed { get; } = DomainCatalog.For(DomainGroup.Steam);

    /// <summary>SNI mà listener của Chế độ PUBG chấp nhận.</summary>
    public static IReadOnlyList<string> Pubg { get; } = DomainCatalog.For(DomainGroup.Pubg);

    /// <summary>Dòng hosts của Chế độ PUBG. Không bao giờ có *xenuine* hay accounts.pubg.com (spec 4.4).</summary>
    public static IReadOnlyList<HostsEntry> PubgHosts { get; } = [.. ProbeTargets.Pubg.Select(host => new HostsEntry("127.0.0.1", host))];
}
