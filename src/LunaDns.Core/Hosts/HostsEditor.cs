using System.Net;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Hosts;

/// <summary>
/// Thêm/gỡ dòng của LunaDNS trong nội dung file hosts, làm việc trên byte: phần còn lại của file
/// (kể cả comment tiếng Việt ở UTF-8 hay ANSI) giữ nguyên từng byte. Không đọc/ghi file (spec 4.4, 9).
/// </summary>
public static class HostsEditor
{
    public const string Marker = "# LunaDNS";

    public static HostsSnapshot Plan(ReadOnlySpan<byte> original, IReadOnlyList<HostsEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0) throw new ArgumentException("Cần ít nhất một dòng hosts.", nameof(entries));
        EnsureSupportedEncoding(original);
        List<string> lines = [.. entries.Select(FormatLine).Distinct(StringComparer.Ordinal)];
        var lineEnding = original.IndexOf("\r\n"u8) < 0 && original.IndexOf((byte)'\n') >= 0 ? "\n" : "\r\n";
        var addedNewline = original.Length > 0 && original[^1] != (byte)'\n';
        return new HostsSnapshot(lines, lineEnding, addedNewline);
    }

    /// <summary>Thêm các dòng còn thiếu vào cuối file. Chạy lại sau crash không nhân đôi dòng.</summary>
    public static byte[] Apply(ReadOnlySpan<byte> current, HostsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureSupportedEncoding(current);
        var eol = Encoding.ASCII.GetBytes(snapshot.LineEnding);
        var missing = new List<byte[]>();
        foreach (var text in snapshot.Lines)
        {
            var line = Encoding.ASCII.GetBytes(text);
            if (FindLine(current, line, eol, 0) < 0) missing.Add(line);
        }
        if (missing.Count == 0) return current.ToArray();

        using var output = new MemoryStream(current.Length + 128);
        output.Write(current);
        if (current.Length > 0 && current[^1] != (byte)'\n') output.Write(eol);
        foreach (var line in missing)
        {
            output.Write(line);
            output.Write(eol);
        }
        return output.ToArray();
    }

    /// <summary>
    /// Gỡ đúng các dòng đã thêm. Nếu các dòng đó vẫn nằm liền nhau ở cuối file (trường hợp thường gặp)
    /// thì trả lại đúng từng byte như trước Apply, kể cả dấu xuống dòng LunaDNS đã thêm. Chạy lại không đổi gì.
    /// </summary>
    public static byte[] Revert(ReadOnlySpan<byte> current, HostsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var eol = Encoding.ASCII.GetBytes(snapshot.LineEnding);
        var block = Encoding.ASCII.GetBytes(string.Concat(snapshot.Lines.Select(l => l + snapshot.LineEnding)));
        if (current.EndsWith(block) && IsLineStart(current, current.Length - block.Length))
        {
            var cut = current.Length - block.Length;
            if (snapshot.AddedNewline && current[..cut].EndsWith(eol)) cut -= eol.Length;
            return current[..cut].ToArray();
        }

        var result = current.ToArray();
        foreach (var text in snapshot.Lines) result = RemoveLine(result, Encoding.ASCII.GetBytes(text), eol);
        return result;
    }

    private static string FormatLine(HostsEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!IPAddress.TryParse(entry.Address, out var address) || !IPAddress.IsLoopback(address))
            throw new ArgumentException("LunaDNS chỉ trỏ domain về chính máy này (địa chỉ loopback).", nameof(entry));
        var host = DomainMatcher.Normalize(entry.Host);
        if (host.Length == 0 || !host.All(IsHostChar) || !DomainMatcher.Matches(host, GameDomains.All))
            throw new ArgumentException("Có domain không thuộc danh sách domain game.", nameof(entry));
        return $"{address} {host} {Marker}";
    }

    private static bool IsHostChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-';

    private static void EnsureSupportedEncoding(ReadOnlySpan<byte> content)
    {
        ReadOnlySpan<byte> utf16Le = [0xFF, 0xFE];
        ReadOnlySpan<byte> utf16Be = [0xFE, 0xFF];
        if (content.StartsWith(utf16Le) || content.StartsWith(utf16Be))
            throw new ConfiguratorException(ConfiguratorError.HostsUnsupportedEncoding,
                "File hosts đang lưu dạng UTF-16 nên LunaDNS không sửa. Hãy lưu lại file hosts dạng UTF-8 hoặc ANSI.");
    }

    private static bool IsLineStart(ReadOnlySpan<byte> content, int index) => index == 0 || content[index - 1] == (byte)'\n';

    /// <summary>Vị trí đầu tiên từ <paramref name="from"/> mà <paramref name="line"/> chiếm trọn một dòng; -1 nếu không có.</summary>
    private static int FindLine(ReadOnlySpan<byte> content, ReadOnlySpan<byte> line, ReadOnlySpan<byte> eol, int from)
    {
        while (from <= content.Length - line.Length)
        {
            var relative = content[from..].IndexOf(line);
            if (relative < 0) return -1;
            var at = from + relative;
            var end = at + line.Length;
            if (IsLineStart(content, at) && (end == content.Length || content[end..].StartsWith(eol))) return at;
            from = at + 1;
        }
        return -1;
    }

    private static byte[] RemoveLine(byte[] content, byte[] line, byte[] eol)
    {
        using var output = new MemoryStream(content.Length);
        var from = 0;
        int at;
        while ((at = FindLine(content, line, eol, from)) >= 0)
        {
            output.Write(content, from, at - from);
            var end = at + line.Length;
            from = end == content.Length ? end : end + eol.Length;
        }
        output.Write(content, from, content.Length - from);
        return output.ToArray();
    }
}
