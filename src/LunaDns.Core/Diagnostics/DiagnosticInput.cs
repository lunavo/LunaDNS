using LunaDns.Core.Domains;
using LunaDns.Core.Journal;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Diagnostics;

/// <summary>
/// Dữ liệu cho báo cáo (spec 4.9). Probe = null nếu chưa kiểm tra. AutoConfig là dòng đã mô tả sẵn
/// (<see cref="DiagnosticReport.DescribeAutoConfig"/> hoặc thông báo lỗi đọc).
/// Labels: tên các mục theo nền tảng; mặc định là nhãn của bản Windows.
/// DomainList: trạng thái danh sách tên miền lớp 2 (null: không hỏi được), spec D4.
/// </summary>
public sealed record DiagnosticInput(
    string AppVersion,
    string WindowsVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ProbeResult>? Probe,
    IReadOnlyList<JournalEntry> Journal,
    IReadOnlyList<string> NrptRules,
    string AutoConfig,
    IReadOnlyList<string> HostsLines,
    IReadOnlyList<string> LogTail,
    DiagnosticLabels? Labels = null,
    DomainListState? DomainList = null);

/// <summary>Nhãn dòng hệ điều hành và hai mục cấu hình mạng. macOS: "/etc/resolver của LunaDNS", "PAC (networksetup)".</summary>
public sealed record DiagnosticLabels(string Os = "Windows", string DnsRoutes = "Luật NRPT của LunaDNS", string Proxy = "AutoConfigURL");
