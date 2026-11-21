using System.Globalization;
using System.Resources;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;

[assembly: NeutralResourcesLanguage("vi")]

namespace LunaDns.App;

/// <summary>
/// Chuỗi giao diện trong Strings.resx (bản trung tính là tiếng Việt, KH5 quyết định 7).
/// Khoá thiếu thì trả "[khoá]" để giao diện không sập; StringsTests bắt các khoá thiếu.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Resources = new("LunaDns.App.Strings", typeof(Strings).Assembly);

    public static string AppTitle => Get(nameof(AppTitle));
    public static string HeaderSubtitle => Get(nameof(HeaderSubtitle));
    public static string CheckButton => Get(nameof(CheckButton));
    public static string CheckingStatus => Get(nameof(CheckingStatus));
    public static string NotCheckedYet => Get(nameof(NotCheckedYet));
    public static string CheckedAtFormat => Get(nameof(CheckedAtFormat));
    public static string CheckFailed => Get(nameof(CheckFailed));
    public static string CheckSectionTitle => Get(nameof(CheckSectionTitle));
    public static string ProbeDetailFormat => Get(nameof(ProbeDetailFormat));
    public static string ProbeCoveredFormat => Get(nameof(ProbeCoveredFormat));
    public static string ProbeMissingFormat => Get(nameof(ProbeMissingFormat));
    public static string DomainProbeOk => Get(nameof(DomainProbeOk));
    public static string DomainProbeNeedsDns => Get(nameof(DomainProbeNeedsDns));
    public static string DomainProbeNeedsSni => Get(nameof(DomainProbeNeedsSni));
    public static string DomainProbeUnreachable => Get(nameof(DomainProbeUnreachable));
    public static string DomainProbeFailed => Get(nameof(DomainProbeFailed));
    public static string CustomDeletedFormat => Get(nameof(CustomDeletedFormat));
    public static string CustomUndoSecondsFormat => Get(nameof(CustomUndoSecondsFormat));
    public static string CustomCountFormat => Get(nameof(CustomCountFormat));
    public static string CustomImportReportFormat => Get(nameof(CustomImportReportFormat));
    public static string CustomProbeDetailFormat => Get(nameof(CustomProbeDetailFormat));
    public static string DomainRejectSyntax => Get(nameof(DomainRejectSyntax));
    public static string DomainRejectPublicSuffixFormat => Get(nameof(DomainRejectPublicSuffixFormat));
    public static string DomainRejectLocal => Get(nameof(DomainRejectLocal));
    public static string DomainRejectDeniedFormat => Get(nameof(DomainRejectDeniedFormat));
    public static string DomainRejectDuplicateFormat => Get(nameof(DomainRejectDuplicateFormat));
    public static string DomainRejectLimitFormat => Get(nameof(DomainRejectLimitFormat));
    public static string AllOk => Get(nameof(AllOk));
    public static string UnreachableHint => Get(nameof(UnreachableHint));
    public static string ApplyRecommendedButton => Get(nameof(ApplyRecommendedButton));
    public static string NothingToApply => Get(nameof(NothingToApply));
    public static string StatusOff => Get(nameof(StatusOff));
    public static string StatusOn => Get(nameof(StatusOn));
    public static string StatusProxyFormat => Get(nameof(StatusProxyFormat));
    public static string OverallOff => Get(nameof(OverallOff));
    public static string OverallOnFormat => Get(nameof(OverallOnFormat));
    public static string OverallBusy => Get(nameof(OverallBusy));
    public static string OverallAttention => Get(nameof(OverallAttention));
    public static string FeatureBusy => Get(nameof(FeatureBusy));
    public static string FeatureStoppedShort => Get(nameof(FeatureStoppedShort));
    public static string DismissMessage => Get(nameof(DismissMessage));
    public static string DiagnosticsButton => Get(nameof(DiagnosticsButton));
    public static string CheckUpdateButton => Get(nameof(CheckUpdateButton));
    public static string UpdateAvailableTitle => Get(nameof(UpdateAvailableTitle));
    public static string UpdateAvailableFormat => Get(nameof(UpdateAvailableFormat));
    public static string UpToDateTitle => Get(nameof(UpToDateTitle));
    public static string UpToDateFormat => Get(nameof(UpToDateFormat));
    public static string UpdateCheckFailed => Get(nameof(UpdateCheckFailed));
    public static string UninstallButton => Get(nameof(UninstallButton));
    public static string UninstallConfirmTitle => Get(nameof(UninstallConfirmTitle));
    public static string UninstallConfirmMessage => Get(nameof(UninstallConfirmMessage));
    public static string DiagnosticsRunning => Get(nameof(DiagnosticsRunning));
    public static string ReportSavedFormat => Get(nameof(ReportSavedFormat));
    public static string ReportFailedFormat => Get(nameof(ReportFailedFormat));
    public static string DisableAllButton => Get(nameof(DisableAllButton));
    public static string DisableAllDone => Get(nameof(DisableAllDone));
    public static string AdvancedHeader => Get(nameof(AdvancedHeader));
    public static string AdvancedHint => Get(nameof(AdvancedHint));
    public static string SplitModeLabel => Get(nameof(SplitModeLabel));
    public static string SplitModeFirstByte => Get(nameof(SplitModeFirstByte));
    public static string SplitModeMidSni => Get(nameof(SplitModeMidSni));
    public static string SplitDelayLabel => Get(nameof(SplitDelayLabel));
    public static string DohLabel => Get(nameof(DohLabel));
    public static string DohCloudflareFirst => Get(nameof(DohCloudflareFirst));
    public static string DohGoogleFirst => Get(nameof(DohGoogleFirst));
    public static string TrayTooltipOff => Get(nameof(TrayTooltipOff));
    public static string TrayTooltipOnFormat => Get(nameof(TrayTooltipOnFormat));
    public static string TrayTooltipBusy => Get(nameof(TrayTooltipBusy));
    public static string TrayTooltipAttention => Get(nameof(TrayTooltipAttention));
    public static string TrayOpen => Get(nameof(TrayOpen));
    public static string TrayQuit => Get(nameof(TrayQuit));
    public static string ConfirmYes => Get(nameof(ConfirmYes));
    public static string ConfirmNo => Get(nameof(ConfirmNo));
    public static string ProxyOverrideTitle => Get(nameof(ProxyOverrideTitle));
    public static string ProxyOverrideText => Get(nameof(ProxyOverrideText));
    public static string FeatureFailedFormat => Get(nameof(FeatureFailedFormat));
    public static string ChangesLeftBehind => Get(nameof(ChangesLeftBehind));
    public static string FeatureStoppedFormat => Get(nameof(FeatureStoppedFormat));
    public static string FeatureStoppedLeftBehindFormat => Get(nameof(FeatureStoppedLeftBehindFormat));
    public static string RestoreFailed => Get(nameof(RestoreFailed));
    public static string StartupRestoredFormat => Get(nameof(StartupRestoredFormat));
    public static string StartupRestoreFailed => Get(nameof(StartupRestoreFailed));
    public static string UnexpectedError => Get(nameof(UnexpectedError));

    public static string ServiceNotInstalled => Get(nameof(ServiceNotInstalled));
    public static string ServiceNotResponding => Get(nameof(ServiceNotResponding));
    public static string InstallServiceButton => Get(nameof(InstallServiceButton));
    public static string ReinstallServiceButton => Get(nameof(ReinstallServiceButton));
    public static string ServiceInstalled => Get(nameof(ServiceInstalled));
    public static string ServiceInstallFailedFormat => Get(nameof(ServiceInstallFailedFormat));
    public static string ServiceCliMissing => Get(nameof(ServiceCliMissing));
    public static string DaemonNeedsUpdate => Get(nameof(DaemonNeedsUpdate));

    public static string DomainsSectionTitle => Get(nameof(DomainsSectionTitle));
    public static string GroupSteam => Get(nameof(GroupSteam));
    public static string GroupOtherGames => Get(nameof(GroupOtherGames));
    public static string GroupReddit => Get(nameof(GroupReddit));
    public static string GroupCustom => Get(nameof(GroupCustom));
    public static string GroupCountFormat => Get(nameof(GroupCountFormat));
    public static string GroupCustomCountFormat => Get(nameof(GroupCustomCountFormat));
    public static string GroupViewButton => Get(nameof(GroupViewButton));
    public static string GroupEditButton => Get(nameof(GroupEditButton));
    public static string CustomDialogTitle => Get(nameof(CustomDialogTitle));
    public static string CustomAddWatermark => Get(nameof(CustomAddWatermark));
    public static string CustomAddButton => Get(nameof(CustomAddButton));
    public static string CustomProbeSelected => Get(nameof(CustomProbeSelected));
    public static string CustomProbeAll => Get(nameof(CustomProbeAll));
    public static string CustomImport => Get(nameof(CustomImport));
    public static string CustomExport => Get(nameof(CustomExport));
    public static string CustomUndo => Get(nameof(CustomUndo));
    public static string CustomSave => Get(nameof(CustomSave));
    public static string CustomCancel => Get(nameof(CustomCancel));
    public static string CustomApplying => Get(nameof(CustomApplying));
    public static string CustomDiscardQuestion => Get(nameof(CustomDiscardQuestion));
    public static string CustomNotChecked => Get(nameof(CustomNotChecked));
    public static string DomainsNewBadge => Get(nameof(DomainsNewBadge));
    public static string DomainsRoutedCountFormat => Get(nameof(DomainsRoutedCountFormat));
    public static string DomainsFootnote => Get(nameof(DomainsFootnote));
    public static string GroupNotChecked => Get(nameof(GroupNotChecked));
    public static string GroupCustomEmpty => Get(nameof(GroupCustomEmpty));
    public static string GroupKeptBack => Get(nameof(GroupKeptBack));
    public static string GroupNotApplied => Get(nameof(GroupNotApplied));
    public static string CustomAddLabel => Get(nameof(CustomAddLabel));
    public static string CustomAddHint => Get(nameof(CustomAddHint));
    public static string CustomAddedFormat => Get(nameof(CustomAddedFormat));
    public static string CustomAddedAllFormat => Get(nameof(CustomAddedAllFormat));
    public static string CustomEmptyTitle => Get(nameof(CustomEmptyTitle));
    public static string CustomEmptyBody => Get(nameof(CustomEmptyBody));
    public static string CustomApplyingDetailFormat => Get(nameof(CustomApplyingDetailFormat));
    public static string CustomSaveFailedTitle => Get(nameof(CustomSaveFailedTitle));
    public static string CustomSaveFailedSuffix => Get(nameof(CustomSaveFailedSuffix));
    public static string CustomDiscardBody => Get(nameof(CustomDiscardBody));
    public static string CustomDiscardKeepEditing => Get(nameof(CustomDiscardKeepEditing));
    public static string CustomDiscardConfirm => Get(nameof(CustomDiscardConfirm));
    public static string CustomEditLabel => Get(nameof(CustomEditLabel));
    public static string CustomEditHintFormat => Get(nameof(CustomEditHintFormat));
    public static string CustomProbeSelectedFormat => Get(nameof(CustomProbeSelectedFormat));
    public static string CustomDeleteFormat => Get(nameof(CustomDeleteFormat));
    public static string CustomFileFailedFormat => Get(nameof(CustomFileFailedFormat));
    public static string GroupListTitleFormat => Get(nameof(GroupListTitleFormat));
    public static string GroupListHeaderFormat => Get(nameof(GroupListHeaderFormat));
    public static string GroupListSubtitle => Get(nameof(GroupListSubtitle));
    public static string GroupListNote => Get(nameof(GroupListNote));
    public static string CloseButton => Get(nameof(CloseButton));
    public static string ProbeCustomSummaryFormat => Get(nameof(ProbeCustomSummaryFormat));
    public static string ProbeCustomHint => Get(nameof(ProbeCustomHint));

    /// <summary>Hậu tố khoá cho câu riêng của macOS (vd. "RestoreFailed_Mac"): có thì dùng thay câu chung.</summary>
    public const string MacSuffix = "_Mac";

    public static string Get(string key) => Lookup(key, OperatingSystem.IsMacOS());

    /// <summary>Tách ra để test kiểm cả câu Windows lẫn câu macOS trên một máy.</summary>
    internal static string Lookup(string key, bool macOS) =>
        (macOS ? Find(key + MacSuffix) : null) ?? Find(key) ?? $"[{key}]";

    private static string? Find(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) is { Length: > 0 } value ? value : null;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public static string Error(FeatureError error) => Get("Error_" + error);

    public static string Verdict(ProbeVerdict verdict) => Get("Verdict_" + verdict);

    public static string FeatureTitle(Feature feature) => Get("Feature_" + feature);

    public static string FeatureDescription(Feature feature) => Get("FeatureDesc_" + feature);

    public static string GroupName(DomainGroup group) => group switch
    {
        DomainGroup.Steam => GroupSteam,
        DomainGroup.OtherGames => GroupOtherGames,
        DomainGroup.Reddit => GroupReddit,
        DomainGroup.Custom => GroupCustom,
        _ => Get("Group_" + group),
    };
}
