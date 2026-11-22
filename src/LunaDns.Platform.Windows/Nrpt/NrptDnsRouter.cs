using System.Net;
using LunaDns.Core.Domains;
using LunaDns.Core.Platform;

namespace LunaDns.Platform.Windows.Nrpt;

/// <summary>
/// Định tuyến DNS bằng NRPT, chỉ trên luật mang dấu của LunaDNS (Comment = Marker trong snapshot, luôn bắt đầu bằng "LunaDNS ").
/// Luật của phần mềm khác (ví dụ Tailscale: GUID cố định, không Comment) không bao giờ bị sửa hay xoá; namespace trùng y hệt
/// với luật lạ thì bỏ qua (spike 2026-11, spec 4.2). Luật lạ cụ thể hơn (ví dụ ".store.steampowered.com") vẫn thắng theo NRPT.
/// </summary>
public sealed class NrptDnsRouter(INrptStore store)
{
    public const string DisplayName = "LunaDNS";
    public const string MarkerPrefix = "LunaDNS ";

    public DnsRouteSnapshot Plan(IReadOnlyList<string> domains, IPAddress nameServer)
    {
        ArgumentNullException.ThrowIfNull(domains);
        ArgumentNullException.ThrowIfNull(nameServer);
        if (!IPAddress.IsLoopback(nameServer))
            throw new ArgumentException("NRPT của LunaDNS chỉ được trỏ về stub trên chính máy này.", nameof(nameServer));

        var wanted = new List<string>();
        foreach (var domain in domains)
        {
            var host = DomainMatcher.Normalize(domain);
            if (!DomainPolicy.IsRoutable(host))
                throw new ArgumentException("Có tên miền không hợp lệ để định tuyến.", nameof(domains));
            wanted.Add("." + host); // mọi subdomain
            wanted.Add(host);       // chính tên gốc: luật hậu tố không chắc khớp tên gốc
        }

        var foreign = store.List()
            .Where(r => !IsLunaDns(r))
            .SelectMany(r => r.Namespaces)
            .Select(n => n.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var distinct = wanted.Distinct(StringComparer.Ordinal).ToList();
        List<string> namespaces = [.. distinct.Where(n => !foreign.Contains(n))];
        List<string> skipped = [.. distinct.Where(foreign.Contains)];
        if (namespaces.Count == 0)
            throw new ConfiguratorException(ConfiguratorError.DnsRouteConflict,
                "Các tên miền đã chọn đều có luật DNS của phần mềm khác (ví dụ VPN). LunaDNS không sửa luật đó nên không bật được Định tuyến DNS.");
        return new DnsRouteSnapshot(MarkerPrefix + Guid.NewGuid().ToString("N"), namespaces, nameServer.ToString(), skipped);
    }

    /// <summary>Chạy lại sau crash không tạo luật thứ hai.</summary>
    public void Apply(DnsRouteSnapshot snapshot)
    {
        EnsureMarker(snapshot);
        if (store.List().Any(r => IsOurs(r, snapshot))) return;
        store.Add(snapshot.Namespaces, snapshot.NameServer, snapshot.Marker, DisplayName);
    }

    public void Revert(DnsRouteSnapshot snapshot)
    {
        EnsureMarker(snapshot);
        foreach (var rule in store.List().Where(r => IsOurs(r, snapshot))) store.Remove(rule.Name);
    }

    /// <summary>
    /// Quét dọn (spec F7): gỡ luật có Comment <c>LunaDNS &lt;id&gt;</c> mà <paramref name="coveredMarkers"/> không giữ.
    /// Luật không Comment (Tailscale) hay Comment khác thì không đụng. Trả số luật đã gỡ.
    /// </summary>
    public int SweepOrphans(IReadOnlySet<string> coveredMarkers)
    {
        ArgumentNullException.ThrowIfNull(coveredMarkers);
        var removed = 0;
        foreach (var rule in store.List())
        {
            if (!IsLunaDns(rule) || rule.Comment!.Length == MarkerPrefix.Length || coveredMarkers.Contains(rule.Comment)) continue;
            store.Remove(rule.Name);
            removed++;
        }
        return removed;
    }

    private static bool IsLunaDns(NrptRule rule) =>
        rule.Comment?.StartsWith(MarkerPrefix, StringComparison.Ordinal) == true;

    private static bool IsOurs(NrptRule rule, DnsRouteSnapshot snapshot) =>
        IsLunaDns(rule) && string.Equals(rule.Comment, snapshot.Marker, StringComparison.Ordinal);

    /// <summary>Snapshot đến từ journal trên đĩa: dấu rỗng hay lạ thì từ chối, để không bao giờ khớp nhầm luật không Comment.</summary>
    private static void EnsureMarker(DnsRouteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.Marker.StartsWith(MarkerPrefix, StringComparison.Ordinal) || snapshot.Marker.Length == MarkerPrefix.Length)
            throw new ArgumentException("Snapshot NRPT không mang dấu của LunaDNS.", nameof(snapshot));
    }
}
