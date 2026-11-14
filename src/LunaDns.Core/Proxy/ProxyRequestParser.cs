using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using LunaDns.Core.Domains;

namespace LunaDns.Core.Proxy;

public static class ProxyRequestParser
{
    public const int MaxHeadLength = 8192;

    /// <summary>Vị trí ngay sau dòng trống kết thúc head, hoặc -1 nếu chưa nhận đủ.</summary>
    public static int FindHeadEnd(ReadOnlySpan<byte> data)
    {
        var index = data.IndexOf("\r\n\r\n"u8);
        return index < 0 ? -1 : index + 4;
    }

    /// <summary>Đọc dòng đầu của head. Header phía sau không được dùng tới.</summary>
    public static bool TryParse(ReadOnlySpan<byte> head, [NotNullWhen(true)] out ProxyRequest? request)
    {
        request = null;
        var lineEnd = head.IndexOf("\r\n"u8);
        if (lineEnd <= 0) return false;
        var line = head[..lineEnd];
        foreach (var b in line)
        {
            if (b < 0x20 || b >= 0x7F) return false;
        }

        var parts = Encoding.ASCII.GetString(line).Split(' ');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0) return false;
        if (parts[2] is not ("HTTP/1.1" or "HTTP/1.0")) return false;
        var method = parts[0];
        foreach (var c in method)
        {
            if (c is < 'A' or > 'Z') return false;
        }

        if (method != "CONNECT")
        {
            request = new ProxyRequest(method, parts[1], null, 0);
            return true;
        }

        var authority = parts[1];
        var colon = authority.LastIndexOf(':');
        if (colon <= 0) return false;
        var host = DomainMatcher.Normalize(authority[..colon]);
        if (host.Length == 0) return false;
        foreach (var c in host)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-')) return false;
        }
        if (!int.TryParse(authority.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port)) return false;
        if (port is < 1 or > 65535) return false;

        request = new ProxyRequest(method, authority, host, port);
        return true;
    }
}
