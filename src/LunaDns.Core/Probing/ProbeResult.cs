using System.Net;

namespace LunaDns.Core.Probing;

/// <summary>Kết quả kiểm tra một host. Hai danh sách địa chỉ dùng cho báo cáo chẩn đoán (spec 4.9).</summary>
public sealed record ProbeResult(
    string Host,
    ProbeVerdict Verdict,
    IReadOnlyList<IPAddress> SystemAddresses,
    IReadOnlyList<IPAddress> DohAddresses,
    bool SystemAddressBlocked);
