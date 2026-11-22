using LunaDns.Core.Features;

namespace LunaDns.App.Features;

/// <summary>
/// U1 (spec 2026-11-22): banner "X đang tắt dù lần trước bạn để bật" kèm nút Bật lại. <see cref="Missing"/> là các tính năng
/// nút Bật lại sẽ bật.
/// </summary>
internal sealed record ResumeNotice(WantedFeatures Missing, string Text)
{
    /// <param name="carried">Tập chụp trước khi cài lại dịch vụ nền (daemon cũ chưa nhớ wanted); None nếu không có.</param>
    /// <param name="dismissed">Tập còn thiếu lúc người dùng đóng banner: còn đúng tập đó thì không hiện lại.</param>
    public static ResumeNotice? From(ControllerAvailability availability, ResumeState? resume, FeatureStatus status, WantedFeatures carried,
        WantedFeatures? dismissed)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(carried);
        if (availability is not (ControllerAvailability.Local or ControllerAvailability.DaemonRunning)) return null;
        if (resume?.Restoring == true) return null;
        var missing = (resume?.Wanted ?? WantedFeatures.None).Union(carried).Missing(status);
        if (missing.IsEmpty() || missing == dismissed) return null;

        var titles = missing.List().Select(Strings.FeatureTitle).ToList();
        var names = titles.Count == 1 ? titles[0] : Strings.Format(nameof(Strings.ResumeBothFormat), titles[0], titles[1]);
        var text = Strings.Format(nameof(Strings.ResumeNoticeFormat), names);
        return new(missing, Reason(resume, missing) is { } reason ? $"{text} {reason}" : text);
    }

    private static string? Reason(ResumeState? resume, WantedFeatures missing)
    {
        if (resume is null) return null;
        if (resume.Failure is { } failure && missing.Contains(failure.Feature))
            return failure.Outcome == FeatureOutcome.NeedsProxyOverride ? Strings.ResumeNoticeProxy : ErrorMessages.For(failure);
        return resume.Enabled ? null : Strings.ResumeNoticeSwitchOff;
    }
}
