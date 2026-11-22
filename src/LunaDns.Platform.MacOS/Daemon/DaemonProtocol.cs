using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Giao thức giữa giao diện/CLI (quyền user) và daemon (root, launchd): mỗi kết nối một dòng JSON yêu cầu, một dòng JSON trả lời.
/// Lệnh: <c>ping</c>, <c>status</c>, <c>enable</c> (kèm Feature), <c>disable</c> (kèm Feature), <c>disable-all</c>,
/// <c>get-domains</c>, <c>set-domains</c> (kèm Domains), <c>get-tuning</c>, <c>set-tuning</c> (kèm Tuning),
/// <c>get-domain-list</c>, <c>set-domain-list</c> (kèm Envelope), <c>set-domain-list-enabled</c> (kèm Enabled),
/// <c>set-auto-resume</c> (kèm Enabled, F1), <c>get-log</c> (kèm Lines tuỳ chọn, F3). <c>disable-all</c> kèm SessionEnd = true (đăng xuất/tắt máy) thì không xoá wanted.
/// </summary>
public static class DaemonProtocol
{
    public const string Label = "vn.lunavo.lunadns";
    public const string SocketPath = "/var/run/lunadns.sock";
    public const int MaxLineBytes = 131072; // set-domain-list: envelope 64 KB → base64 ~88 KB + JSON

    public const string Ping = "ping";
    public const string Status = "status";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string DisableAll = "disable-all";
    public const string GetDomains = "get-domains";
    public const string SetDomains = "set-domains";
    public const string GetTuning = "get-tuning";
    public const string SetTuning = "set-tuning";
    public const string GetDomainList = "get-domain-list";
    public const string SetDomainList = "set-domain-list";
    public const string SetDomainListEnabled = "set-domain-list-enabled";
    public const string SetAutoResume = "set-auto-resume";
    public const string GetLog = "get-log";

    /// <summary>Đầu câu trả lời cho lệnh daemon không biết. Bản cũ cũng dùng đúng câu này, nên giao diện nhận ra daemon cần cài lại.</summary>
    public const string UnknownCommandPrefix = "Lệnh lạ:";

    /// <summary>
    /// Tuỳ chọn dây: camelCase, bỏ trường null, enum dạng chuỗi camelCase. Metadata lấy từ <see cref="DaemonJsonContext"/>
    /// (sinh sẵn, để bản publish trimmed chạy được); kiểu ngoài context thì ném NotSupportedException thay vì dùng reflection.
    /// </summary>
    public static JsonSerializerOptions Json { get; } = CreateJson();

    internal static JsonTypeInfo<DaemonRequest> RequestInfo { get; } = (JsonTypeInfo<DaemonRequest>)Json.GetTypeInfo(typeof(DaemonRequest));

    internal static JsonTypeInfo<DaemonResponse> ResponseInfo { get; } = (JsonTypeInfo<DaemonResponse>)Json.GetTypeInfo(typeof(DaemonResponse));

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter<DomainGroup>(JsonNamingPolicy.CamelCase),
                new JsonStringEnumConverter<DohProvider>(JsonNamingPolicy.CamelCase),
                new JsonStringEnumConverter<SplitMode>(JsonNamingPolicy.CamelCase),
            },
            TypeInfoResolver = DaemonJsonContext.Default,
        };
        options.MakeReadOnly();
        return options;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DaemonRequest))]
[JsonSerializable(typeof(DaemonResponse))]
[JsonSerializable(typeof(DaemonTuning))]
[JsonSerializable(typeof(DaemonDomainList))]
[JsonSerializable(typeof(DaemonDomainListResult))]
[JsonSerializable(typeof(DaemonResume))]
internal sealed partial class DaemonJsonContext : JsonSerializerContext;

/// <summary>
/// Feature là khoá trong FeatureKeys ("dns-routing", "sni-proxy"). SessionEnd chỉ dùng với disable-all: app thoát vì đăng
/// xuất/tắt máy, không phải người dùng thoát, nên daemon giữ wanted (F1). Daemon cũ bỏ qua trường này.
/// Lines: số dòng log muốn lấy với get-log (null: tối đa).
/// </summary>
public sealed record DaemonRequest(string Command, string? Feature = null, bool AllowProxyOverride = false, DomainSelection? Domains = null,
    DaemonTuning? Tuning = null, string? Envelope = null, bool? Enabled = null, bool? SessionEnd = null, int? Lines = null);

/// <summary>
/// Outcome/Error là tên enum FeatureOutcome/FeatureError dạng chuỗi (để client cũ không vỡ khi thêm giá trị).
/// Message là câu tiếng Việt cho người dùng khi Ok = false. Log: đuôi log đã che của get-log (F3).
/// </summary>
public sealed record DaemonResponse(
    bool Ok,
    string? Message = null,
    string? Version = null,
    DaemonStatus? Status = null,
    string? Outcome = null,
    string? Error = null,
    DomainSelection? Domains = null,
    DaemonTuning? Tuning = null,
    DaemonDomainList? DomainList = null,
    DaemonDomainListResult? DomainListResult = null,
    DaemonResume? Resume = null,
    string[]? Log = null);

/// <summary>Máy DoH và tổ hợp cắt gói của daemon (D3). Enum dạng chuỗi camelCase ("quad9", "midSni").</summary>
public sealed record DaemonTuning(DohProvider Doh, SplitMode SplitMode, bool SplitRecords, int SplitDelayMs)
{
    public static DaemonTuning From(DohProvider doh, TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new(doh, candidate.Split.Mode, candidate.Split.Records, candidate.DelayMs);
    }

    public static DaemonTuning From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return From(settings.EffectiveDoh(), settings.CurrentCandidate());
    }

    /// <summary>Hàm, không phải thuộc tính, để không ra JSON.</summary>
    public TuneCandidate ToCandidate() => new(new HelloSplit(SplitMode, SplitRecords), SplitDelayMs);
}

public sealed record DaemonStatus(bool DnsRouting, bool SniProxy, int? ProxyPort, int ProxyConnections, int JournalEntries);

/// <summary>Trạng thái lớp 2 của daemon (D4). Added theo khoá nhóm của file ("steam"), để không phụ thuộc cách ghi enum.</summary>
public sealed record DaemonDomainList(bool Enabled, long Serial, DateTimeOffset? Issued, DateTimeOffset? CheckedAt, Dictionary<string, string[]> Added)
{
    public static DaemonDomainList From(DomainListState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.Enabled, state.Serial, state.Issued, state.CheckedAt,
            state.Added.ToDictionary(kv => DomainListVerifier.GroupKey(kv.Key), kv => kv.Value.ToArray(), StringComparer.Ordinal));
    }

    public DomainListState ToState()
    {
        var added = new Dictionary<DomainGroup, IReadOnlyList<string>>();
        foreach (var (key, list) in Added ?? [])
            if (DomainListVerifier.TryParseGroup(key, out var group) && list is not null) added[group] = list;
        return new(Enabled, Serial, Issued, CheckedAt, added);
    }
}

/// <summary>Kết quả set-domain-list / set-domain-list-enabled. Outcome, Error là tên enum dạng chuỗi.</summary>
public sealed record DaemonDomainListResult(string Outcome, int Added, int Dropped, string? Error, bool PreviousRestored)
{
    public static DaemonDomainListResult From(DomainListOfferResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new(r.Outcome.ToString(), r.Added, r.Dropped, r.Error == FeatureError.None ? null : r.Error.ToString(), r.PreviousRestored);
    }

    public DomainListOfferResult ToResult() => new(
        Enum.TryParse<DomainListOutcome>(Outcome, out var o) ? o : DomainListOutcome.Unavailable,
        Added, Dropped,
        Enum.TryParse<FeatureError>(Error, out var e) ? e : FeatureError.None,
        PreviousRestored);
}

/// <summary>
/// F1: công tắc Tự bật lại, wanted, đang tự bật dở hay không, và lỗi lần tự bật cuối (FailedFeature là khoá FeatureKeys,
/// Outcome/Error là tên enum dạng chuỗi). Có trong mọi câu trả lời kèm Status của daemon; null với daemon cũ và "lunadns run".
/// </summary>
public sealed record DaemonResume(bool Enabled, bool DnsRouting, bool SniProxy, bool Restoring, string? FailedFeature = null, string? Outcome = null,
    string? Error = null)
{
    public static DaemonResume From(ResumeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var failure = state.Failure;
        return new(state.Enabled, state.Wanted.DnsRouting, state.Wanted.SniProxy, state.Restoring,
            failure is null ? null : FeatureKeys.For(failure.Feature),
            failure?.Outcome.ToString(),
            failure is null || failure.Error == FeatureError.None ? null : failure.Error.ToString());
    }

    public ResumeState ToState()
    {
        FeatureResult? failure = null;
        if (FeatureKeys.TryParse(FailedFeature, out var feature))
        {
            var outcome = Enum.TryParse<FeatureOutcome>(Outcome, out var o) && Enum.IsDefined(o) ? o : FeatureOutcome.Failed;
            var error = Enum.TryParse<FeatureError>(Error, out var e) && Enum.IsDefined(e) ? e : FeatureError.None;
            if (outcome == FeatureOutcome.Failed && error == FeatureError.None) error = FeatureError.Other;
            failure = new FeatureResult(feature, outcome, error);
        }
        return new(Enabled, new WantedFeatures(DnsRouting, SniProxy), Restoring, failure);
    }
}
