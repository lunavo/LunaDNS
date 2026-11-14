using System.Text;
using LunaDns.Core.Diagnostics;
using LunaDns.Core.Hosts;
using LunaDns.Platform.Windows.Hosts;
using LunaDns.Platform.Windows.Nrpt;
using LunaDns.Platform.Windows.Proxy;

namespace LunaDns.Platform.Windows.Diagnostics;

/// <summary>
/// Đọc trạng thái hệ thống cho báo cáo chẩn đoán (spec 4.9). Chỉ đọc. Mỗi phần lỗi riêng thành một dòng
/// "(không đọc được: …)" để báo cáo vẫn có các phần còn lại.
/// </summary>
public sealed class WindowsDiagnostics(INrptStore nrpt, AutoProxyRegistry proxy, HostsFileStore hosts)
{
    public static WindowsDiagnostics CreateDefault() =>
        new(new NrptCimStore(), new AutoProxyRegistry(), new HostsFileStore(HostsFileStore.DefaultPath()));

    public static string WindowsVersion() => Environment.OSVersion.VersionString;

    /// <summary>Luật NRPT có Comment bắt đầu bằng "LunaDNS ", kể cả luật mồ côi không còn trong Journal (KH5 quyết định 10).</summary>
    public IReadOnlyList<string> LunaDnsNrptRules() => Read(() =>
        nrpt.List()
            .Where(r => r.Comment?.StartsWith(NrptDnsRouter.MarkerPrefix, StringComparison.Ordinal) == true)
            .Select(r => $"{r.Comment} | {string.Join(", ", r.Namespaces)} → {string.Join(", ", r.NameServers)}")
            .ToList());

    /// <summary>Dòng AutoConfigURL đã che (chỉ hiện PAC loopback, KH5 quyết định 9).</summary>
    public string AutoConfigLine(string? userSid = null) =>
        Read(() => [DiagnosticReport.DescribeAutoConfig(proxy.ReadCurrent(userSid))])[0];

    /// <summary>Các dòng hosts có "# LunaDNS". Dòng của LunaDNS chỉ có ASCII nên đọc bằng UTF-8 là đủ.</summary>
    public IReadOnlyList<string> MarkedHostsLines() => Read(() =>
        Encoding.UTF8.GetString(hosts.Read())
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Contains(HostsEditor.Marker, StringComparison.Ordinal))
            .ToList());

    /// <summary>Ranh giới một mục báo cáo: lỗi gì cũng thành một dòng chữ, không làm hỏng cả báo cáo.</summary>
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
