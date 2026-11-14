using LunaDns.Core.Journal;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Diagnostics;

/// <summary>
/// Dữ liệu cho báo cáo (spec 4.9). Probe = null nếu chưa kiểm tra. AutoConfig là dòng đã mô tả sẵn
/// (<see cref="DiagnosticReport.DescribeAutoConfig"/> hoặc thông báo lỗi đọc).
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
    IReadOnlyList<string> LogTail);
