using System.Net;

namespace LunaDns.Core.Dns;

/// <summary>
/// Máy chủ DoH (RFC 8484). Kết nối đi thẳng tới <see cref="BootstrapAddresses"/> để khỏi hỏi DNS hệ thống,
/// vì DNS đó có thể đã bị đầu độc. TLS vẫn kiểm tra chứng chỉ theo host của <see cref="Endpoint"/>.
/// </summary>
public sealed record DohServer(string Name, Uri Endpoint, IReadOnlyList<IPAddress> BootstrapAddresses)
{
    public static DohServer Cloudflare { get; } = new("Cloudflare", new Uri("https://cloudflare-dns.com/dns-query"),
        [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.0.0.1")]);

    public static DohServer Google { get; } = new("Google", new Uri("https://dns.google/dns-query"),
        [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4")]);

    /// <summary>Biến thể không lọc của Quad9 (D2).</summary>
    public static DohServer Quad9 { get; } = new("Quad9", new Uri("https://dns.quad9.net/dns-query"),
        [IPAddress.Parse("9.9.9.9"), IPAddress.Parse("149.112.112.112")]);

    /// <summary>Biến thể không lọc của AdGuard (D2), để không vô tình chặn tên miền game.</summary>
    public static DohServer AdGuard { get; } = new("AdGuard", new Uri("https://unfiltered.adguard-dns.com/dns-query"),
        [IPAddress.Parse("94.140.14.140"), IPAddress.Parse("94.140.14.141")]);

    /// <summary>Cloudflare trước, dự phòng Google (spec 4.2). Danh mục đầy đủ ở <see cref="DohCatalog"/>.</summary>
    public static IReadOnlyList<DohServer> Defaults { get; } = [Cloudflare, Google];
}
