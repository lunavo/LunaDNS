using System.Globalization;
using System.Text;
using LunaDns.Core.Diagnostics;
using LunaDns.Core.Hosts;
using LunaDns.Core.Journal;
using LunaDns.Platform.MacOS;

namespace LunaDns.App.MacOS;

/// <summary>
/// Báo cáo trên macOS. Chỉ đọc, bằng quyền user: /etc/resolver và /etc/hosts đọc được, <c>networksetup -getautoproxyurl</c> không cần root.
/// Journal nằm trong thư mục root 700 của dịch vụ nền: báo cáo chỉ ghi số mục daemon trả về, cạnh phiên bản hệ điều hành.
/// </summary>
internal sealed class MacReportDiagnostics(MacNetworkConfigurator configurator, DaemonFeatureController? daemon) : IPlatformDiagnostics
{
    public DiagnosticLabels Labels { get; } = new("Hệ điều hành", "File /etc/resolver của LunaDNS", "PAC (networksetup)");

    public string OsVersion()
    {
        var daemonText = daemon switch
        {
            null => "dịch vụ nền: không rõ",
            { Availability: var a, DaemonVersion: var v, JournalEntries: var j } when a == Features.ControllerAvailability.DaemonRunning =>
                string.Create(CultureInfo.InvariantCulture, $"dịch vụ nền {v ?? "?"}, journal {j ?? 0} mục"),
            { Availability: var a } => $"dịch vụ nền: {a}",
        };
        return string.Create(CultureInfo.InvariantCulture, $"macOS {Environment.OSVersion.Version} ({daemonText})");
    }

    public IReadOnlyList<JournalEntry> Journal() => [];

    public IReadOnlyList<string> DnsRouteLines() => Read(() =>
        [.. configurator.Dns.ListOurs().Select(d => string.Create(CultureInfo.InvariantCulture,
            $"{configurator.Dns.Directory}/{d} → 127.0.0.1:{configurator.Dns.Port}"))]);

    public string ProxyLine() => Read(() =>
    {
        var services = configurator.Proxy.ServicesUsingLunaDnsPac();
        return [services.Count == 0 ? "(không service nào dùng PAC của LunaDNS)" : "PAC LunaDNS trên: " + string.Join(", ", services)];
    })[0];

    public IReadOnlyList<string> MarkedHostsLines() => Read(() =>
        [.. Encoding.UTF8.GetString(configurator.Hosts.Read()).Split('\n')
            .Where(line => line.Contains(HostsEditor.Marker, StringComparison.Ordinal))]);

    private static IReadOnlyList<string> Read(Func<IReadOnlyList<string>> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return [$"(không đọc được: {ex.Message})"];
        }
    }
}
