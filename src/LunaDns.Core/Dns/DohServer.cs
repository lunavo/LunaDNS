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

    /// <summary>Cloudflare trước, dự phòng Google (spec 4.2). KH5 cho đổi thứ tự trong mục Nâng cao.</summary>
    public static IReadOnlyList<DohServer> Defaults { get; } = [Cloudflare, Google];
}
