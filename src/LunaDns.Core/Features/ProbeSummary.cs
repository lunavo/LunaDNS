using LunaDns.Core.Domains;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Features;

/// <summary>Nhóm đã được biện pháp đang bật che chưa (KH6 U1, sửa UX-1).</summary>
public enum ProbeCoverageState
{
    /// <summary>Nhóm bình thường, không cần biện pháp nào.</summary>
    NotNeeded,
    /// <summary>Còn biện pháp đề xuất đang tắt (ghi ở Missing).</summary>
    Missing,
    /// <summary>Mọi biện pháp đề xuất đang bật. Prober đo đường mạng gốc, nên đây là "biết", chưa "đo" (KH6 D4).</summary>
    Covered,
    /// <summary>Có host không vào được kể cả khi cắt gói; biện pháp không giúp được.</summary>
    Unreachable,
}

/// <summary>Missing: biện pháp đề xuất còn tắt. Covering: biện pháp ghi ở hậu tố "đang né bằng …" (bảng KH6 3.2).</summary>
public sealed record ProbeCoverage(ProbeCoverageState State, ProbeRemedies Missing, ProbeRemedies Covering);

public sealed record ProbeGroupSummary(DomainGroup Group, IReadOnlyList<ProbeResult> Results)
{
    /// <summary>Verdict nặng nhất của nhóm, theo thứ tự Ok &lt; DnsPoisoned &lt; SniFiltered &lt; Unreachable.</summary>
    public ProbeVerdict Worst => Results.Count == 0 ? ProbeVerdict.Ok : Results.Max(r => r.Verdict);

    /// <summary>
    /// Đối chiếu biện pháp đề xuất (ProbeAdvisor) với biện pháp đang bật. Khi đã che đủ, Covering là biện pháp ứng với
    /// verdict nặng nhất: SNI thì Proxy SNI, DNS thì Định tuyến DNS (đúng câu chữ bảng KH6 3.2).
    /// </summary>
    public ProbeCoverage Coverage(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (Worst == ProbeVerdict.Unreachable) return new(ProbeCoverageState.Unreachable, ProbeRemedies.None, ProbeRemedies.None);
        var required = ProbeAdvisor.Recommend(Results);
        if (required == ProbeRemedies.None) return new(ProbeCoverageState.NotNeeded, ProbeRemedies.None, ProbeRemedies.None);
        var missing = required & ~status.Remedies;
        if (missing != ProbeRemedies.None) return new(ProbeCoverageState.Missing, missing, ProbeRemedies.None);
        var covering = Worst == ProbeVerdict.SniFiltered ? ProbeRemedies.SniProxy : ProbeRemedies.DnsRouting;
        return new(ProbeCoverageState.Covered, ProbeRemedies.None, covering);
    }
}

/// <summary>Kết quả "Kiểm tra" gom theo nhóm Steam/PUBG cho màn hình chính (spec 4.8).</summary>
public sealed record ProbeSummary(IReadOnlyList<ProbeGroupSummary> Groups, DateTimeOffset CheckedAt)
{
    public bool AnyUnreachable => Groups.Any(g => g.Worst == ProbeVerdict.Unreachable);

    public static ProbeSummary Create(IReadOnlyList<ProbeResult> results, DateTimeOffset checkedAt)
    {
        ArgumentNullException.ThrowIfNull(results);
        var pubg = results.Where(IsPubg).ToList();
        var groups = new List<ProbeGroupSummary> { new(DomainGroup.Steam, [.. results.Where(r => !IsPubg(r))]) };
        if (pubg.Count > 0) groups.Add(new ProbeGroupSummary(DomainGroup.Pubg, pubg));
        return new ProbeSummary(groups, checkedAt);
    }

    /// <summary>Biện pháp cho nhóm Steam (bảng spec 4.1), bỏ những gì đang bật. Dùng cho nút "Áp dụng đề xuất".</summary>
    public ProbeRemedies PendingSteamRemedies(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        var remedies = ProbeAdvisor.Recommend(Groups.Where(g => g.Group == DomainGroup.Steam).SelectMany(g => g.Results));
        return remedies & ~status.Remedies;
    }

    /// <summary>PUBG bị chặn kiểu DNS/SNI và Chế độ PUBG đang tắt: chỉ gợi ý, không tự bật (KH5 quyết định 5).</summary>
    public bool SuggestPubgMode(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return !status.PubgMode && Groups.Any(g => g.Group == DomainGroup.Pubg
            && g.Results.Any(r => r.Verdict is ProbeVerdict.DnsPoisoned or ProbeVerdict.SniFiltered));
    }

    private static bool IsPubg(ProbeResult result) =>
        ProbeTargets.Pubg.Contains(result.Host, StringComparer.OrdinalIgnoreCase);
}
