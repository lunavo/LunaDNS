using LunaDns.Core.Diagnostics;
using LunaDns.Core.Journal;

namespace LunaDns.App;

/// <summary>
/// Trạng thái hệ thống cho báo cáo chẩn đoán (spec 4.9), chỉ đọc. Windows: luật NRPT, AutoConfigURL, hosts, journal ở %ProgramData%.
/// macOS: file /etc/resolver của LunaDNS, service đang dùng PAC LunaDNS, hosts; journal của dịch vụ nền thuộc root nên chỉ có số mục.
/// Mỗi phần lỗi riêng thành một dòng "(không đọc được: …)".
/// </summary>
internal interface IPlatformDiagnostics
{
    /// <summary>Tên các mục trong báo cáo. Windows: nhãn mặc định (NRPT, AutoConfigURL) để báo cáo cũ không đổi.</summary>
    DiagnosticLabels Labels { get; }

    string OsVersion();

    IReadOnlyList<JournalEntry> Journal();

    IReadOnlyList<string> DnsRouteLines();

    string ProxyLine();

    IReadOnlyList<string> MarkedHostsLines();
}
