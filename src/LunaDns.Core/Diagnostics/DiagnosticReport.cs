using System.Globalization;
using System.Net;
using System.Text;

namespace LunaDns.Core.Diagnostics;

/// <summary>Nội dung LunaDNS-report-&lt;ts&gt;.txt (spec 4.9). Toàn bộ văn bản đi qua <see cref="Redactor"/> trước khi trả.</summary>
public static class DiagnosticReport
{
    public const int LogLines = 200;

    public static string Build(DiagnosticInput input, Redactor redactor)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(redactor);
        var sb = new StringBuilder();
        sb.AppendLine("LunaDNS — báo cáo chẩn đoán");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Thời điểm: {input.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Phiên bản LunaDNS: {input.AppVersion}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Windows: {input.WindowsVersion}");

        Section(sb, "Kết quả kiểm tra");
        if (input.Probe is null)
        {
            sb.AppendLine("(chưa chạy)");
        }
        else
        {
            foreach (var r in input.Probe)
            {
                var blocked = r.SystemAddressBlocked ? " (IP chặn)" : string.Empty;
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{r.Host}: {r.Verdict} | DNS hệ thống: {Addresses(r.SystemAddresses)}{blocked} | DoH: {Addresses(r.DohAddresses)}");
            }
        }

        Section(sb, string.Create(CultureInfo.InvariantCulture, $"Journal ({input.Journal.Count} mục)"));
        if (input.Journal.Count == 0) sb.AppendLine("(trống)");
        foreach (var e in input.Journal)
            sb.AppendLine(CultureInfo.InvariantCulture, $"{e.AppliedAt:O} {e.Feature} {e.Op} {e.Id} {e.Snapshot.GetRawText()}");

        Section(sb, "Luật NRPT của LunaDNS");
        Lines(sb, input.NrptRules);
        Section(sb, "AutoConfigURL");
        sb.AppendLine(input.AutoConfig);
        Section(sb, "Dòng hosts có # LunaDNS");
        Lines(sb, input.HostsLines);
        Section(sb, string.Create(CultureInfo.InvariantCulture, $"{LogLines} dòng log cuối"));
        Lines(sb, [.. input.LogTail.TakeLast(LogLines)]);

        return redactor.Redact(sb.ToString());
    }

    /// <summary>Chỉ ghi URL PAC loopback (của LunaDNS); PAC công ty hay giá trị lạ không ra khỏi máy (KH5 quyết định 9).</summary>
    public static string DescribeAutoConfig(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "(trống)";
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsLoopback
            ? value
            : "(có giá trị khác của người dùng, không ghi vào báo cáo)";
    }

    private static void Section(StringBuilder sb, string title) =>
        sb.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"== {title} ==");

    private static void Lines(StringBuilder sb, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) sb.AppendLine("(không có)");
        foreach (var line in lines) sb.AppendLine(line);
    }

    private static string Addresses(IReadOnlyList<IPAddress> addresses) =>
        addresses.Count == 0 ? "(không có)" : string.Join(", ", addresses);
}
