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

    private static string Names(ProbeRemedies remedies)
    {
        var names = new List<string>(2);
        if (remedies.HasFlag(ProbeRemedies.DnsRouting)) names.Add(Strings.FeatureTitle(Feature.DnsRouting));
        if (remedies.HasFlag(ProbeRemedies.SniProxy)) names.Add(Strings.FeatureTitle(Feature.SniProxy));
        return string.Join(", ", names);
    }
}
