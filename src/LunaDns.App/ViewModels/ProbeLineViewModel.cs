using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

namespace LunaDns.App.ViewModels;

/// <summary>
/// Một dòng kết quả "Kiểm tra" (KH6 3.2, sửa UX-1): kết luận của Prober kèm hậu tố về biện pháp đang bật.
/// "Đang né bằng …" chứ không "đã khắc phục": Prober đo đường gốc, app chỉ biết biện pháp đang bật (D4).
/// </summary>
internal sealed record ProbeLineViewModel(string Group, string Detail, Tone Tone)
{
    public string AutomationName => $"{Group}: {Detail}";

    public static ProbeLineViewModel From(ProbeGroupSummary group, FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (group.Group == DomainGroup.Custom && ForCustom(group) is { } custom) return custom;
        var coverage = group.Coverage(status);
        var (suffix, tone) = coverage.State switch
        {
            ProbeCoverageState.Covered => (Strings.Format(nameof(Strings.ProbeCoveredFormat), Names(coverage.Covering)), Tone.Good),
            ProbeCoverageState.Missing => (Strings.Format(nameof(Strings.ProbeMissingFormat), Names(coverage.Missing)), Tone.Warning),
            ProbeCoverageState.Unreachable => (string.Empty, Tone.Error),
            _ => (string.Empty, Tone.Good),
        };
        var verdict = Strings.Verdict(group.Worst);
        var detail = suffix.Length == 0 ? verdict : Strings.Format(nameof(Strings.ProbeDetailFormat), verdict, suffix);
        return new ProbeLineViewModel(Strings.GroupName(group.Group), detail, tone);
    }

    /// <summary>
    /// Tuỳ chỉnh gồm nhiều trang không liên quan nên đếm thay cho một kết luận (mockup 1.2): DNS/SNI là "giúp được",
    /// Unreachable là "không giúp được". Toàn bộ bình thường thì dùng dòng thường.
    /// </summary>
    private static ProbeLineViewModel? ForCustom(ProbeGroupSummary group)
    {
        var helped = group.Results.Count(r => r.Verdict is ProbeVerdict.DnsPoisoned or ProbeVerdict.SniFiltered);
        var unhelped = group.Results.Count(r => r.Verdict == ProbeVerdict.Unreachable);
        if (helped + unhelped == 0) return null;
        var detail = Strings.Format(nameof(Strings.ProbeCustomSummaryFormat), helped, unhelped);
        return new ProbeLineViewModel(Strings.GroupName(group.Group), detail, unhelped > 0 ? Tone.Error : Tone.Good);
    }

    private static string Names(ProbeRemedies remedies)
    {
        var names = new List<string>(2);
        if (remedies.HasFlag(ProbeRemedies.DnsRouting)) names.Add(Strings.FeatureTitle(Feature.DnsRouting));
        if (remedies.HasFlag(ProbeRemedies.SniProxy)) names.Add(Strings.FeatureTitle(Feature.SniProxy));
        return string.Join(", ", names);
    }
}
