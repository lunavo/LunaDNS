using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace LunaDns.Core.Dns;

/// <summary>Kiểm URL và IP của DoH tự nhập, dựng <see cref="DohServer"/>. Hàm thuần, không đụng mạng.</summary>
public static class CustomDohParser
{
    public const string ServerName = "Tự nhập";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    public static bool TryParseEndpoint(string? text, [NotNullWhen(true)] out Uri? endpoint)
    {
        endpoint = null;
        if (!Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)) return false;
        if (uri.UserInfo.Length > 0 || uri.Fragment.Length > 0) return false;
        endpoint = uri;
        return true;
    }

    /// <summary>IPv4/IPv6 ngăn bằng dấu phẩy, chấm phẩy hoặc khoảng trắng. Một giá trị sai thì lỗi cả ô.</summary>
    public static bool TryParseAddresses(string? text, out IReadOnlyList<IPAddress> addresses)
    {
        var parsed = new List<IPAddress>();
        addresses = parsed;
        foreach (var token in (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Parse(token) is not { } address) return false;
            parsed.Add(address);
        }
        return true;
    }

    public static IPAddress? LiteralAddress(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
            ? IPAddress.Parse(endpoint.DnsSafeHost)
            : null;
    }

    /// <summary>IP tay → IP literal của host → IP đã phân giải. URL hỏng hoặc không có IP nào thì <c>null</c>.</summary>
    public static DohServer? ToServer(CustomDoh? custom)
    {
        if (custom is null || !TryParseEndpoint(custom.Endpoint, out var endpoint)) return null;
        IReadOnlyList<IPAddress> addresses = ParseAll(custom.ManualAddresses);
        if (addresses.Count == 0 && LiteralAddress(endpoint) is { } literal) addresses = [literal];
        if (addresses.Count == 0) addresses = ParseAll(custom.ResolvedAddresses);
        return addresses.Count == 0 ? null : new DohServer(ServerName, endpoint, addresses);
    }

    /// <summary>Dữ liệu từ settings.json (có thể sửa tay): bỏ giá trị hỏng thay vì lỗi.</summary>
    private static List<IPAddress> ParseAll(IReadOnlyList<string>? values) =>
        [.. (values ?? []).Select(Parse).OfType<IPAddress>()];

    /// <summary><see cref="IPAddress.TryParse(string, out IPAddress)"/> nhận cả "1" (0.0.0.1): bắt buộc có '.' hoặc ':'.</summary>
    private static IPAddress? Parse(string? token) =>
        token is not null && token.AsSpan().IndexOfAny('.', ':') >= 0
        && IPAddress.TryParse(token, out var address)
        && address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
            ? address
            : null;
}
