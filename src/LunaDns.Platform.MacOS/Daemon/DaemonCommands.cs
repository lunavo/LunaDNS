using LunaDns.Core.Features;
using LunaDns.Core.Journal;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>Ánh xạ yêu cầu socket sang FeatureSet. Chế độ PUBG chưa mở trên macOS.</summary>
public sealed class DaemonCommands(FeatureSet features, ChangeJournal journal, string version)
{
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
            default:
                return new DaemonResponse(false, $"Lệnh lạ: {request.Command}");
        }
    }

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
            "Các domain Steam đã có file /etc/resolver của phần mềm khác (VPN hoặc script tự tạo). Gỡ file đó rồi thử lại.",
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
