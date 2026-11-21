namespace LunaDns.Core.Features;

/// <summary>Mã lỗi để giao diện chọn thông báo tiếng Việt (ErrorMessages trong App).</summary>
public enum FeatureError
{
    None,
    /// <summary>127.0.0.1:53 đang bị app khác giữ (PortInUse hoặc AccessDenied, ghi chú KH2).</summary>
    DnsPortBusy,
    /// <summary>Không còn cổng trống trong 18080–18090.</summary>
    ProxyPortsBusy,
    /// <summary>127.0.0.1:443 đang bị app khác giữ.</summary>
    PubgPortBusy,
    ListenerFailed,
    AccessDenied,
    DnsRouteConflict,
    /// <summary>Chỉ xuất hiện khi hoàn tác cũng lỗi; bình thường trả Outcome NeedsProxyOverride.</summary>
    ExistingProxyConfig,
    UserHiveNotLoaded,
    UnexpectedProxyValue,
    HostsUnsupportedEncoding,
    NrptFailed,
    RollbackFailed,
    /// <summary>macOS: giao diện không nói chuyện được với dịch vụ nền (chưa cài, chưa chạy, hoặc từ chối kết nối).</summary>
    ServiceUnavailable,
    /// <summary>Danh sách tên miền định tuyến rỗng (spec KH8 5.1).</summary>
    NoDomains,
    Other,
}
