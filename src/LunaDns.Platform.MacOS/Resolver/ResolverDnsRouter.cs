using System.Globalization;
using System.Net;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Platform;

namespace LunaDns.Platform.MacOS.Resolver;

/// <summary>
/// Định tuyến DNS bằng file <c>/etc/resolver/&lt;domain&gt;</c> (resolver(5)): mỗi domain một file, áp dụng cho chính domain
/// và mọi subdomain. Dòng đầu là <c># &lt;Marker&gt;</c>; chỉ file mang đúng dấu này mới bị sửa hay xoá. File có sẵn của
/// phần mềm khác (VPN, script tay) thì bỏ qua và ghi vào SkippedNamespaces, giống luật NRPT lạ trên Windows.
/// Chỉ thị <c>port</c> cho phép stub nghe cổng cao.
/// </summary>
public sealed class ResolverDnsRouter
{
    public const string DefaultDirectory = "/etc/resolver";
    public const string MarkerPrefix = "LunaDNS ";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public ResolverDnsRouter(int port, string directory = DefaultDirectory)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        Port = port;
        Directory = directory;
    }

    public int Port { get; }

    public string Directory { get; }

    public DnsRouteSnapshot Plan(IReadOnlyList<string> domains, IPAddress nameServer)
    {
        ArgumentNullException.ThrowIfNull(domains);
        ArgumentNullException.ThrowIfNull(nameServer);
        if (!IPAddress.IsLoopback(nameServer))
            throw new ArgumentException("Định tuyến DNS của LunaDNS chỉ được trỏ về stub trên chính máy này.", nameof(nameServer));

        var wanted = new List<string>();
        foreach (var domain in domains)
        {
            var host = DomainMatcher.Normalize(domain);
            if (!IsSafeDomain(host))
                throw new ArgumentException("Có tên miền không hợp lệ để định tuyến.", nameof(domains));
            if (!wanted.Contains(host, StringComparer.Ordinal)) wanted.Add(host);
        }

        List<string> namespaces = [];
        List<string> skipped = [];
        foreach (var host in wanted)
        {
            var marker = ReadMarker(PathFor(host));
            if (marker is null || IsLunaDnsMarker(marker)) namespaces.Add(host); // file của LunaDNS cũ còn sót: Apply ghi đè
            else skipped.Add(host);
        }
        if (namespaces.Count == 0)
            throw new ConfiguratorException(ConfiguratorError.DnsRouteConflict,
                "Các tên miền đã chọn đều có file /etc/resolver của phần mềm khác (VPN hoặc script tự tạo). LunaDNS không sửa file đó nên không bật được Định tuyến DNS.");
        return new DnsRouteSnapshot(MarkerPrefix + Guid.NewGuid().ToString("N"), namespaces, nameServer.ToString(), skipped);
    }

    /// <summary>Chạy lại sau crash không đổi gì. File lạ xuất hiện sau Plan thì giữ nguyên.</summary>
    public void Apply(DnsRouteSnapshot snapshot)
    {
        EnsureSnapshot(snapshot);
        Guard(() =>
        {
            System.IO.Directory.CreateDirectory(Directory);
            var content = Utf8.GetBytes(Content(snapshot));
            foreach (var host in snapshot.Namespaces)
            {
                var path = PathFor(host);
                var marker = ReadMarker(path);
                if (marker is not null && !IsLunaDnsMarker(marker)) continue;
                if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content)) continue;
                AtomicFile.Write(path, content, AtomicFile.WorldReadable);
            }
        });
    }

    public void Revert(DnsRouteSnapshot snapshot)
    {
        EnsureSnapshot(snapshot, requireRoutable: false);
        Guard(() =>
        {
            foreach (var host in snapshot.Namespaces)
            {
                var path = PathFor(host);
                if (string.Equals(ReadMarker(path), snapshot.Marker, StringComparison.Ordinal)) File.Delete(path);
            }
        });
    }

    /// <summary>Các file mang dấu LunaDNS đang có, để báo trạng thái và chẩn đoán.</summary>
    public IReadOnlyList<string> ListOurs()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        return [.. System.IO.Directory.EnumerateFiles(Directory)
            .Where(p => ReadMarker(p) is { } m && IsLunaDnsMarker(m))
            .Select(Path.GetFileName)
            .OfType<string>()
            .Order(StringComparer.Ordinal)];
    }

    internal string Content(DnsRouteSnapshot snapshot) =>
        string.Create(CultureInfo.InvariantCulture, $"# {snapshot.Marker}\nnameserver {snapshot.NameServer}\nport {Port}\n");

    private string PathFor(string host) => Path.Combine(Directory, host);

    /// <summary>Dấu ở dòng đầu (bỏ "# "), chuỗi rỗng nếu file không có dấu, null nếu chưa có file.</summary>
    private static string? ReadMarker(string path)
    {
        if (!File.Exists(path)) return null;
        using var reader = new StreamReader(path, Utf8);
        var first = reader.ReadLine() ?? "";
        return first.StartsWith("# ", StringComparison.Ordinal) ? first[2..].Trim() : "";
    }

    private static bool IsLunaDnsMarker(string marker) =>
        marker.StartsWith(MarkerPrefix, StringComparison.Ordinal) && marker.Length > MarkerPrefix.Length;

    /// <summary>Tên file lấy từ journal trên đĩa: chỉ tên miền định tuyến được (DomainPolicy), không bao giờ có "/" hay "..".</summary>
    private static bool IsSafeDomain(string host) => IsSafeFileName(host) && DomainPolicy.IsRoutable(host);

    /// <summary>Chỉ xét tên file: Revert không phụ thuộc DomainPolicy vì danh sách cấm/PSL đổi theo bản phát hành, journal cũ vẫn phải gỡ được.</summary>
    private static bool IsSafeFileName(string host) =>
        host.Length > 0
        && host.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '.')
        && !host.StartsWith('.') && !host.Contains("..", StringComparison.Ordinal);

    private static void EnsureSnapshot(DnsRouteSnapshot snapshot, bool requireRoutable = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsLunaDnsMarker(snapshot.Marker) || snapshot.Marker.Contains('\n', StringComparison.Ordinal))
            throw new ArgumentException("Snapshot định tuyến DNS không mang dấu của LunaDNS.", nameof(snapshot));
        if (snapshot.Namespaces.Any(n => requireRoutable ? !IsSafeDomain(n) : !IsSafeFileName(n)))
            throw new ArgumentException("Snapshot định tuyến DNS có tên miền lạ.", nameof(snapshot));
        if (!IPAddress.TryParse(snapshot.NameServer, out var server) || !IPAddress.IsLoopback(server))
            throw new ArgumentException("Snapshot định tuyến DNS không trỏ về máy này.", nameof(snapshot));
    }

    private static void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.AccessDenied,
                "Không có quyền sửa /etc/resolver. LunaDNS cần chạy bằng quyền quản trị (sudo).", ex);
        }
        catch (IOException ex)
        {
            throw new ConfiguratorException(ConfiguratorError.Other, "Không ghi được thư mục /etc/resolver. Hãy thử lại.", ex);
        }
    }
}
