using System.Globalization;
using System.Text;
using LunaDns.Core.Diagnostics;
using LunaDns.Core.Domains;
using LunaDns.Core.Logging;
using LunaDns.Core.Probing;

namespace LunaDns.App;

/// <summary>
/// Ghi LunaDNS-report-&lt;ts&gt;.txt (spec 4.9) dạng UTF-8 có BOM để Notepad cũ đọc đúng tiếng Việt.
/// <paramref name="serviceLog"/>: đuôi log của dịch vụ nền macOS (F3); null trên Windows.
/// Bản Windows đọc journal bằng JsonFileJournalStore: file hỏng sẽ bị đổi tên .corrupt-&lt;ts&gt; (KH5 quyết định 15).
/// </summary>
internal static partial class ReportWriter
{
    public static string Write(AppPaths paths, IReadOnlyList<ProbeResult>? probe, IPlatformDiagnostics diagnostics, DateTimeOffset? time = null,
        DomainListState? domainList = null, IReadOnlyList<string>? serviceLog = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(diagnostics);
        var now = time ?? DateTimeOffset.Now;
        var input = new DiagnosticInput(
            typeof(ReportWriter).Assembly.GetName().Version?.ToString() ?? "?",
            diagnostics.OsVersion(),
            now,
            probe,
            diagnostics.Journal(),
            diagnostics.DnsRouteLines(),
            diagnostics.ProxyLine(),
            diagnostics.MarkedHostsLines(),
            RollingFileLoggerProvider.ReadTail(paths.LogDirectory, DiagnosticReport.LogLines),
            diagnostics.Labels,
            domainList,
            serviceLog);
        var text = DiagnosticReport.Build(input, new Redactor(Environment.UserName, Environment.MachineName));

        Directory.CreateDirectory(paths.ReportDirectory);
        var path = Path.Combine(paths.ReportDirectory, string.Create(CultureInfo.InvariantCulture, $"LunaDNS-report-{now:yyyyMMdd-HHmmss}.txt"));
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }
}
