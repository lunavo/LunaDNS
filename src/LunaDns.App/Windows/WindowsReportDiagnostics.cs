using LunaDns.Core.Diagnostics;
using LunaDns.Core.Journal;
using LunaDns.Core.Probing;
using LunaDns.Platform.Windows.Diagnostics;

namespace LunaDns.App;

internal sealed class WindowsReportDiagnostics(WindowsDiagnostics diagnostics, string journalFile) : IPlatformDiagnostics
{
    public DiagnosticLabels Labels { get; } = new();

    public string OsVersion() => WindowsDiagnostics.WindowsVersion();

    public IReadOnlyList<JournalEntry> Journal() => new JsonFileJournalStore(journalFile).Load();

    public IReadOnlyList<string> DnsRouteLines() => diagnostics.LunaDnsNrptRules();

    public string ProxyLine() => diagnostics.AutoConfigLine();

    public IReadOnlyList<string> MarkedHostsLines() => diagnostics.MarkedHostsLines();
}

internal static partial class ReportWriter
{
    public static string Write(AppPaths paths, IReadOnlyList<ProbeResult>? probe, WindowsDiagnostics diagnostics, DateTimeOffset? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return Write(paths, probe, new WindowsReportDiagnostics(diagnostics, paths.JournalFile), time);
    }
}
