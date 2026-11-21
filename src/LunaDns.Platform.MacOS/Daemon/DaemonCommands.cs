using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Settings;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Ánh xạ yêu cầu socket sang FeatureSet. Chế độ PUBG chưa mở trên macOS.
/// Daemon giữ settings.json (thư mục dữ liệu của root); giao diện chỉ đọc/ghi danh sách tên miền qua get-domains/set-domains.
/// </summary>
public sealed class DaemonCommands(FeatureSet features, ChangeJournal journal, string version, JsonFileSettingsStore settingsStore)
{
    private AppSettings _settings = (settingsStore ?? throw new ArgumentNullException(nameof(settingsStore))).Load();

    public async Task<DaemonResponse> HandleAsync(DaemonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Command)
        {
            case DaemonProtocol.Ping:
                return new DaemonResponse(true, Version: version);
            case DaemonProtocol.Status:
                return Ok();
            case DaemonProtocol.Enable:
                {
                    if (Parse(request.Feature) is not { } feature) return BadFeature(request.Feature);
                    var result = await features.EnableAsync(feature, request.AllowProxyOverride, cancellationToken).ConfigureAwait(false);
                    return From(result);
                }
            case DaemonProtocol.Disable:
                {
                    if (Parse(request.Feature) is not { } feature) return BadFeature(request.Feature);
                    return From(await features.DisableAsync(feature).ConfigureAwait(false));
                }
            case DaemonProtocol.DisableAll:
                {
                    var result = await features.DisableAllAsync().ConfigureAwait(false);
                    return result.Success
                        ? Ok()
                        : Ok() with { Ok = false, Message = $"Còn {result.RemainingEntries} thay đổi chưa hoàn tác; xem log." };
                }
            case DaemonProtocol.GetDomains:
                return Ok() with { Domains = _settings.Domains };
            case DaemonProtocol.SetDomains:
                return await SetDomainsAsync(request.Domains, cancellationToken).ConfigureAwait(false);
            default:
                return new DaemonResponse(false, $"{DaemonProtocol.UnknownCommandPrefix} {request.Command}");
        }
    }

    /// <summary>
    /// Không tin dữ liệu từ giao diện (quyền user): kiểm lại toàn bộ, áp, rồi mới ghi file (spec KH8 3.3).
    /// Ghi file lỗi thì áp lại danh sách cũ, để tính năng đang chạy khớp với settings.json.
    /// </summary>
    private async Task<DaemonResponse> SetDomainsAsync(DomainSelection? wanted, CancellationToken cancellationToken)
    {
        // Lỗi dữ liệu kèm Status (như lỗi ReapplyAsync): giao diện chỉ coi câu trả lời không có Status là bị từ chối.
        if (wanted is null) return Refused("Thiếu danh sách tên miền.");
        if (DescribeShape(wanted.CheckShape()) is { } shapeError) return Refused(shapeError);
        if (wanted.FirstInvalidIndex() is { } bad)
            return Refused(bad >= DomainPolicy.MaxCustomDomains
                ? $"Tối đa {DomainPolicy.MaxCustomDomains} tên miền."
                : $"Tên miền không hợp lệ ở dòng {bad + 1}.");
        var result = await features.ReapplyAsync(FeatureDomains.Routed(wanted), cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return Ok() with { Ok = false, Error = result.Error.ToString(), Message = DescribeReapply(result) };
        var updated = _settings with { Domains = wanted };
        try
        {
            settingsStore.Save(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var back = await features.ReapplyAsync(FeatureDomains.Routed(_settings.Domains), CancellationToken.None).ConfigureAwait(false);
            return Ok() with
            {
                Ok = false,
                Message = back.Success
                    ? $"Không lưu được settings.json ({ex.Message}); đã giữ danh sách cũ."
                    : $"Không lưu được settings.json ({ex.Message}) và không quay về được danh sách cũ; chạy: sudo lunadns cleanup",
            };
        }
        _settings = updated;
        return Ok() with { Domains = wanted };
    }

    private static string? DescribeShape(DomainSelectionDefect defect) => defect switch
    {
        DomainSelectionDefect.None => null,
        DomainSelectionDefect.MissingGroups => "Thiếu danh sách nhóm tên miền.",
        DomainSelectionDefect.MissingCustom => "Thiếu danh sách tên miền Tuỳ chỉnh.",
        DomainSelectionDefect.UnknownGroup => "Có nhóm tên miền không xác định.",
        DomainSelectionDefect.DuplicateGroup => "Có nhóm tên miền bị lặp.",
        _ => "Danh sách tên miền không đúng định dạng.",
    };

    private static string DescribeReapply(ReapplyResult result) => result.Error switch
    {
        FeatureError.NoDomains => "Chưa có tên miền nào được chọn. Bật ít nhất một nhóm, hoặc tắt Định tuyến DNS và Proxy SNI trước.",
        _ when result.PreviousRestored => $"Không áp được danh sách mới ({result.Error}); đã giữ danh sách cũ.",
        _ => $"Không áp được danh sách mới ({result.Error}) và không khôi phục được; chạy: sudo lunadns cleanup",
    };

    private DaemonResponse Refused(string message) => Ok() with { Ok = false, Message = message };

    private DaemonResponse Ok()
    {
        var s = features.GetStatus();
        return new DaemonResponse(true, Version: version,
            Status: new DaemonStatus(s.DnsRouting, s.SniProxy, s.ProxyPort, s.ProxyConnections, journal.Entries.Count));
    }

    private DaemonResponse From(FeatureResult result) =>
        Ok() with
        {
            Ok = result.Outcome is FeatureOutcome.Enabled or FeatureOutcome.Disabled or FeatureOutcome.Unchanged,
            Outcome = result.Outcome.ToString(),
            Error = result.Error == FeatureError.None ? null : result.Error.ToString(),
            Message = Describe(result),
        };

    private static string? Describe(FeatureResult result) => result switch
    {
        { Outcome: FeatureOutcome.NeedsProxyOverride } =>
            "Máy đang dùng PAC hoặc proxy thủ công. Bật lại với --allow-proxy-override để LunaDNS thay tạm cấu hình đó.",
        { Error: FeatureError.DnsPortBusy } => "Cổng 127.0.0.1:15353 đang bị chương trình khác dùng.",
        { Error: FeatureError.ProxyPortsBusy } => "Các cổng 18080–18090 đều đang bị chương trình khác dùng.",
        { Error: FeatureError.DnsRouteConflict } =>
            "Các tên miền đã chọn đã có file /etc/resolver của phần mềm khác (VPN hoặc script tự tạo). Gỡ file đó rồi thử lại.",
        { Error: FeatureError.AccessDenied } => "Dịch vụ nền không đủ quyền đổi cấu hình hệ thống.",
        { Error: FeatureError.RollbackFailed } => "Bật lỗi và hoàn tác cũng lỗi; chạy: sudo lunadns cleanup",
        { Outcome: FeatureOutcome.Failed } => $"Không bật được ({result.Error}); xem log.",
        _ => null,
    };

    private static Feature? Parse(string? key) => key switch
    {
        FeatureKeys.DnsRouting => Feature.DnsRouting,
        FeatureKeys.SniProxy => Feature.SniProxy,
        _ => null,
    };

    private static DaemonResponse BadFeature(string? key) =>
        new(false, key == FeatureKeys.PubgMode ? "Chế độ PUBG chưa hỗ trợ trên macOS." : $"Tính năng lạ: {key}");
}
