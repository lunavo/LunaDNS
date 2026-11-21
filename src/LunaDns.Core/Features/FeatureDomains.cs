using LunaDns.Core.Domains;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Features;

/// <summary>Domain mà mỗi tính năng tác động (KH5 quyết định 1 và 3).</summary>
public static class FeatureDomains
{
    /// <summary>Tên miền mà Định tuyến DNS (stub + NRPT/resolver) và Proxy SNI (PAC, CONNECT) áp dụng (spec KH8 3.1).</summary>
    public static IReadOnlyList<string> Routed(DomainSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var groups = selection.EnabledGroups.Where(g => g != DomainGroup.Pubg).Distinct();
        var custom = selection.IsEnabled(DomainGroup.Custom)
            ? selection.Custom.Where(c => c.Enabled).Select(c => c.Domain)
            : [];
        var all = groups.SelectMany(DomainCatalog.For).Concat(custom)
            .Select(DomainMatcher.Normalize)
            .Where(DomainPolicy.IsRoutable)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return [.. all.Where(d => !all.Any(p => p != d && DomainMatcher.Matches(d, [p])))];
    }

    /// <summary>SNI mà listener của Chế độ PUBG chấp nhận.</summary>
    public static IReadOnlyList<string> Pubg { get; } = DomainCatalog.For(DomainGroup.Pubg);

    /// <summary>Dòng hosts của Chế độ PUBG. Không bao giờ có *xenuine* hay accounts.pubg.com (spec 4.4).</summary>
    public static IReadOnlyList<HostsEntry> PubgHosts { get; } = [.. ProbeTargets.Pubg.Select(host => new HostsEntry("127.0.0.1", host))];
}
