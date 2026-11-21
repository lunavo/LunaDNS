using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Ánh xạ yêu cầu socket sang FeatureSet. Chế độ PUBG chưa mở trên macOS.
/// Daemon giữ settings.json (thư mục dữ liệu của root); giao diện chỉ đọc/ghi danh sách tên miền và cấu hình cắt gói
/// qua get-domains/set-domains/get-tuning/set-tuning.
/// </summary>
public sealed partial class DaemonCommands
{
    private readonly FeatureSet features;
    private readonly ChangeJournal journal;
    private readonly string version;
    private readonly SettingsHolder _settings;
    private readonly DomainListService _domainList;

    public DaemonCommands(FeatureSet features, ChangeJournal journal, string version, JsonFileSettingsStore settingsStore, DomainListService domainList,
        ILogger? logger = null)
        : this(features, journal, version, new SettingsHolder(settingsStore ?? throw new ArgumentNullException(nameof(settingsStore))), domainList, logger)
    {
    }

    /// <summary>Daemon thật: dùng chung SettingsHolder với Engine, để set-tuning đổi được DoH/tuning lúc dịch vụ khởi động lại.</summary>
    public DaemonCommands(FeatureSet features, ChangeJournal journal, string version, SettingsHolder settings, DomainListService domainList, ILogger? logger = null)
    {
        this.features = features;
        this.journal = journal;
        this.version = version;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _domainList = domainList ?? throw new ArgumentNullException(nameof(domainList));
        // Daemon là bên giữ settings.json trên macOS: dòng bị bỏ lúc nạp phải để lại dấu (spec mục 7).
        if (settings.DroppedDomains > 0) LogDroppedDomains(logger ?? NullLogger.Instance, settings.DroppedDomains);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Đã bỏ {Count} dòng tên miền Tuỳ chỉnh không hợp lệ khi nạp settings.json.")]
    private static partial void LogDroppedDomains(ILogger logger, int count);

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
                return Ok() with { Domains = _settings.Current.Domains };
            case DaemonProtocol.SetDomains:
                return await SetDomainsAsync(request.Domains, cancellationToken).ConfigureAwait(false);
            case DaemonProtocol.GetTuning:
                return Ok() with { Tuning = DaemonTuning.From(_settings.Current) };
            case DaemonProtocol.SetTuning:
                return await SetTuningAsync(request.Tuning, cancellationToken).ConfigureAwait(false);
            case DaemonProtocol.GetDomainList:
                return Ok() with { DomainList = DaemonDomainList.From(_domainList.State) };
            case DaemonProtocol.SetDomainList:
                return await SetDomainListAsync(request.Envelope, cancellationToken).ConfigureAwait(false);
            case DaemonProtocol.SetDomainListEnabled:
                return await SetDomainListEnabledAsync(request.Enabled, cancellationToken).ConfigureAwait(false);
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
        // Routed tính bằng catalog đúng lúc áp, trong cổng của DomainListService (spec D4).
        return await _domainList.WithCatalogAsync(async catalog =>
        {
            var result = await features.ReapplyAsync(FeatureDomains.Routed(wanted, catalog), cancellationToken).ConfigureAwait(false);
            if (!result.Success)
                return Ok() with { Ok = false, Error = result.Error.ToString(), Message = DescribeReapply(result) };
            var updated = _settings.Current with { Domains = wanted };
            try
            {
                _settings.Save(updated);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var back = await features.ReapplyAsync(FeatureDomains.Routed(_settings.Current.Domains, catalog), CancellationToken.None).ConfigureAwait(false);
                return Ok() with
                {
                    Ok = false,
                    Message = back.Success
                        ? $"Không lưu được settings.json ({ex.Message}); đã giữ danh sách cũ."
                        : $"Không lưu được settings.json ({ex.Message}) và không quay về được danh sách cũ; chạy: sudo lunadns cleanup",
                };
            }
            return Ok() with { Domains = wanted };
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>D4: không tin app. Daemon xác minh lại chữ ký, serial, DomainPolicy bằng khoá nhúng trong chính nó.</summary>
    private async Task<DaemonResponse> SetDomainListAsync(string? envelope, CancellationToken cancellationToken)
    {
        if (envelope is null) return Refused("Thiếu danh sách tên miền.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(envelope);
        }
        catch (FormatException)
        {
            return Refused("Danh sách tên miền không đọc được.");
        }
        var result = await _domainList.OfferAsync(bytes, cancellationToken).ConfigureAwait(false);
        return Ok() with
        {
            Ok = result.Success,
            DomainListResult = DaemonDomainListResult.From(result),
            DomainList = DaemonDomainList.From(_domainList.State),
        };
    }

    /// <summary>Áp trước, lưu settings.json sau; lưu lỗi thì bật/tắt ngược lại để tính năng khớp file.</summary>
    private async Task<DaemonResponse> SetDomainListEnabledAsync(bool? enabled, CancellationToken cancellationToken)
    {
        if (enabled is not { } value) return Refused("Thiếu giá trị công tắc.");
        var result = await _domainList.SetEnabledAsync(value, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return Ok() with { Ok = false, DomainListResult = DaemonDomainListResult.From(result), DomainList = DaemonDomainList.From(_domainList.State) };
        try
        {
            _settings.Save(_settings.Current with { DomainListUpdates = value });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _domainList.SetEnabledAsync(!value, CancellationToken.None).ConfigureAwait(false);
            return Ok() with { Ok = false, Message = $"Không lưu được settings.json ({ex.Message}); giữ công tắc cũ." };
        }
        return Ok() with { DomainListResult = DaemonDomainListResult.From(result), DomainList = DaemonDomainList.From(_domainList.State) };
    }

    /// <summary>
    /// D3. Không tin dữ liệu từ giao diện: kiểm, đặt tạm settings mới để dịch vụ khởi động lại đọc được, chạy lại, rồi mới ghi file.
    /// Chạy lại hay ghi file lỗi thì đặt lại settings cũ và chạy lại lần nữa.
    /// </summary>
    private async Task<DaemonResponse> SetTuningAsync(DaemonTuning? wanted, CancellationToken cancellationToken)
    {
        if (wanted is null) return Refused("Thiếu cấu hình cắt gói.");
        if (DescribeTuning(wanted) is { } error) return Refused(error);
        var previous = _settings.Current;
        var updated = previous.WithTuning(wanted.Doh, wanted.ToCandidate());
        _settings.Current = updated;
        // Lỗi thì FeatureSet đặt lại settings cũ và bật lại đúng các tính năng đã chạy trước đó, trong cùng một lượt giữ cổng.
        var result = await features.ReapplyTuningAsync(() => _settings.Current = previous, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            var restored = result.PreviousRestored;
            return Ok() with
            {
                Ok = false,
                Error = result.Error.ToString(),
                Message = restored
                    ? $"Không áp được cấu hình mới ({result.Error}); đã giữ cấu hình cũ."
                    : $"Không áp được cấu hình mới ({result.Error}) và không khôi phục được; chạy: sudo lunadns cleanup",
            };
        }
        try
        {
            _settings.Save(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var restored = await RestoreTuningAsync(previous).ConfigureAwait(false);
            return Ok() with
            {
                Ok = false,
                Message = restored
                    ? $"Không lưu được settings.json ({ex.Message}); đã giữ cấu hình cũ."
                    : $"Không lưu được settings.json ({ex.Message}) và không quay về được cấu hình cũ; chạy: sudo lunadns cleanup",
            };
        }
        return Ok() with { Tuning = DaemonTuning.From(updated) };
    }

    private async Task<bool> RestoreTuningAsync(AppSettings previous)
    {
        _settings.Current = previous;
        return (await features.ReapplyTuningAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false)).Success;
    }

    /// <summary>macOS không có ô Tự nhập DoH, nên không nhận Custom.</summary>
    private static string? DescribeTuning(DaemonTuning tuning) =>
        !Enum.IsDefined(tuning.Doh) || tuning.Doh == DohProvider.Custom ? "Máy chủ DoH không hợp lệ."
        : !Enum.IsDefined(tuning.SplitMode) ? "Điểm cắt gói không hợp lệ."
        : tuning.SplitDelayMs is < 0 or > AppSettings.MaxSplitDelayMs ? $"Độ trễ phải trong 0–{AppSettings.MaxSplitDelayMs} ms."
        : null;

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
