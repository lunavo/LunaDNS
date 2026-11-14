using System.Text;
using System.Text.RegularExpressions;

namespace LunaDns.Core.Proxy;

public static partial class PacBuilder
{
    [GeneratedRegex("^[a-z0-9.-]+$")]
    private static partial Regex SafeDomain();

    public static string Build(int port, IEnumerable<string> domains)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        var list = new List<string>();
        foreach (var d in domains)
        {
            if (!SafeDomain().IsMatch(d))
                throw new ArgumentException($"Domain không hợp lệ cho PAC: '{d}'", nameof(domains));
            list.Add($"\"{d}\"");
        }

        var sb = new StringBuilder();
        sb.Append("function FindProxyForURL(url, host) {\n");
        // Proxy chỉ hiểu CONNECT (spec 4.3): URL không mã hoá phải đi thẳng, nếu không trình duyệt sẽ kẹt ở lỗi của proxy.
        sb.Append("  if (url.substring(0, 6) != \"https:\" && url.substring(0, 4) != \"wss:\") return \"DIRECT\";\n");
        sb.Append("  host = host.toLowerCase();\n");
        sb.Append("  var d = [").Append(string.Join(", ", list)).Append("];\n");
        sb.Append("  for (var i = 0; i < d.length; i++) {\n");
        sb.Append("    if (host == d[i] || dnsDomainIs(host, \".\" + d[i])) return \"PROXY 127.0.0.1:")
          .Append(port).Append("; DIRECT\";\n");
        sb.Append("  }\n");
        sb.Append("  return \"DIRECT\";\n");
        sb.Append("}\n");
        return sb.ToString();
    }
}
