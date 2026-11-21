using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Giao thức giữa giao diện/CLI (quyền user) và daemon (root, launchd): mỗi kết nối một dòng JSON yêu cầu, một dòng JSON trả lời.
/// Lệnh: <c>ping</c>, <c>status</c>, <c>enable</c> (kèm Feature), <c>disable</c> (kèm Feature), <c>disable-all</c>,
/// <c>get-domains</c>, <c>set-domains</c> (kèm Domains), <c>get-tuning</c>, <c>set-tuning</c> (kèm Tuning).
/// </summary>
public static class DaemonProtocol
{
    public const string Label = "vn.lunavo.lunadns";
    public const string SocketPath = "/var/run/lunadns.sock";
    public const int MaxLineBytes = 65536; // set-domains: 200 tên miền × tối đa 253 ký tự

    public const string Ping = "ping";
    public const string Status = "status";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string DisableAll = "disable-all";
    public const string GetDomains = "get-domains";
    public const string SetDomains = "set-domains";
    public const string GetTuning = "get-tuning";
    public const string SetTuning = "set-tuning";

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
internal sealed partial class DaemonJsonContext : JsonSerializerContext;

/// <summary>Feature là khoá trong FeatureKeys ("dns-routing", "sni-proxy").</summary>
public sealed record DaemonRequest(string Command, string? Feature = null, bool AllowProxyOverride = false, DomainSelection? Domains = null,
    DaemonTuning? Tuning = null);

/// <summary>
/// Outcome/Error là tên enum FeatureOutcome/FeatureError dạng chuỗi (để client cũ không vỡ khi thêm giá trị).
/// Message là câu tiếng Việt cho người dùng khi Ok = false.
/// </summary>
public sealed record DaemonResponse(
    bool Ok,
    string? Message = null,
    string? Version = null,
    DaemonStatus? Status = null,
    string? Outcome = null,
    string? Error = null,
    DomainSelection? Domains = null,
    DaemonTuning? Tuning = null);

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
