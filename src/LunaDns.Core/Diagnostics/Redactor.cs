using System.Globalization;
using System.Text.RegularExpressions;

namespace LunaDns.Core.Diagnostics;

/// <summary>
/// Che thông tin cá nhân trước khi khách gửi báo cáo qua chat (spec 4.9, KH5 quyết định 9): tên user, tên máy, SID,
/// IP LAN (10/8, 172.16/12, 192.168/16, 100.64/10, 169.254/16 thành a.b.x.x) và IPv6 ULA/link-local.
/// IP công cộng (Steam, DoH) và loopback được giữ vì cần cho hỗ trợ.
/// </summary>
public sealed partial class Redactor
{
    private readonly Regex? _user;
    private readonly Regex? _machine;

    public Redactor(string userName, string machineName)
    {
        _user = WholeWord(userName);
        _machine = WholeWord(machineName);
    }

    public string Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_user is not null) text = _user.Replace(text, "<user>");
        if (_machine is not null) text = _machine.Replace(text, "<host>");
        text = SidPattern().Replace(text, "<sid>");
        text = Ipv4Pattern().Replace(text, MaskIpv4);
        return Ipv6LanPattern().Replace(text, "<ipv6-lan>");
    }

    private static Regex? WholeWord(string value) => string.IsNullOrWhiteSpace(value)
        ? null
        : new Regex($@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(value.Trim())}(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string MaskIpv4(Match match)
    {
        var a = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var b = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var lan = a == 10
            || (a == 172 && b is >= 16 and <= 31)
            || (a == 192 && b == 168)
            || (a == 100 && b is >= 64 and <= 127)
            || (a == 169 && b == 254);
        return lan ? $"{a}.{b}.x.x" : match.Value;
    }

    [GeneratedRegex(@"S-1-5-21(?:-\d+){3,4}")]
    private static partial Regex SidPattern();

    [GeneratedRegex(@"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?!\.?\d)")]
    private static partial Regex Ipv4Pattern();

    [GeneratedRegex(@"(?<![0-9a-f:])(?:fe80|f[cd][0-9a-f]{2}):[0-9a-f:]*[0-9a-f]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6LanPattern();
}
