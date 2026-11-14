using System.Text.Json;
using System.Text.Json.Serialization;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Giao thức giữa giao diện/CLI (quyền user) và daemon (root, launchd): mỗi kết nối một dòng JSON yêu cầu, một dòng JSON trả lời.
/// Lệnh: <c>ping</c>, <c>status</c>, <c>enable</c> (kèm Feature), <c>disable</c> (kèm Feature), <c>disable-all</c>.
/// </summary>
public static class DaemonProtocol
{
    public const string Label = "vn.lunavo.lunadns";
    public const string SocketPath = "/var/run/lunadns.sock";
    public const int MaxLineBytes = 4096;

    public const string Ping = "ping";
    public const string Status = "status";
    public const string Enable = "enable";
    public const string Disable = "disable";
    public const string DisableAll = "disable-all";

    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Feature là khoá trong FeatureKeys ("dns-routing", "sni-proxy").</summary>
public sealed record DaemonRequest(string Command, string? Feature = null, bool AllowProxyOverride = false);

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
    string? Error = null);

public sealed record DaemonStatus(bool DnsRouting, bool SniProxy, int? ProxyPort, int ProxyConnections, int JournalEntries);
