using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace LunaDns.Core.Net;

/// <summary>Định danh một mạng (spec hồ sơ theo mạng): băm MAC gateway + loại interface; không có MAC thì băm IP gateway + adapter (yếu).</summary>
public sealed record NetworkId(string Value, bool Weak)
{
    /// <summary>8 ký tự đầu, chỉ dùng cho log.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Short is the log-friendly prefix, not the System.Int16 type.")]
    public string Short => Value[..8];

    public static NetworkId? From(string? mac, string? gatewayIp, string kind, string adapter)
    {
        if (string.IsNullOrWhiteSpace(gatewayIp)) return null;
        var normalized = NormalizeMac(mac);
        return normalized is not null
            ? new NetworkId(Hash($"mac|{normalized}|{kind}"), Weak: false)
            : new NetworkId(Hash($"ip|{gatewayIp.Trim()}|{adapter}"), Weak: true);
    }

    /// <summary>"AA-BB-CC-1-2-3" hay "0:1a:2b:3:4:5" thành "aa:bb:cc:01:02:03"; MAC không hợp lệ, toàn 0 hoặc broadcast thì null.</summary>
    public static string? NormalizeMac(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var parts = raw.Trim().Split(':', '-');
        if (parts.Length != 6) return null;
        var bytes = new byte[6];
        for (var i = 0; i < 6; i++)
        {
            if (parts[i].Length is < 1 or > 2
                || !byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i])) return null;
        }
        if (bytes.All(b => b == 0) || bytes.All(b => b == 0xFF)) return null;
        return string.Join(':', bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

/// <summary>Mạng hiện tại: định danh và nhãn gợi ý cho giao diện (vd. "Wi-Fi · 192.168.1.1").</summary>
public sealed record NetworkSnapshot(NetworkId Id, string Label);
