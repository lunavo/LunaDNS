using System.Globalization;

namespace LunaDns.Core.Domains;

/// <summary>
/// Public Suffix List (publicsuffix.org, MPL-2.0), dùng cả phần ICANN lẫn PRIVATE. Chỉ trả lời "tên này có phải hậu tố công cộng
/// không" để chặn tên miền quá rộng (spec KH8 4.2). Cập nhật file .dat mỗi lần phát hành.
/// </summary>
public sealed class PublicSuffixList
{
    private const string ResourceName = "LunaDns.Core.public_suffix_list.dat";
    private static readonly IdnMapping Idn = new();
    private static readonly Lazy<PublicSuffixList> Embedded = new(LoadEmbedded);

    private readonly HashSet<string> _rules = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wildcards = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exceptions = new(StringComparer.Ordinal);

    private PublicSuffixList()
    {
    }

    public static PublicSuffixList Default => Embedded.Value;

    public static PublicSuffixList Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var list = new PublicSuffixList();
        while (reader.ReadLine() is { } line)
        {
            var rule = line.Trim();
            if (rule.Length == 0 || rule.StartsWith("//", StringComparison.Ordinal)) continue;
            var space = rule.IndexOfAny([' ', '\t']);
            if (space >= 0) rule = rule[..space];
            if (rule.StartsWith('!')) list._exceptions.Add(ToAscii(rule[1..]));
            else if (rule.StartsWith("*.", StringComparison.Ordinal)) list._wildcards.Add(ToAscii(rule[2..]));
            else list._rules.Add(ToAscii(rule));
        }
        return list;
    }

    public bool IsPublicSuffix(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_exceptions.Contains(host)) return false;
        if (_rules.Contains(host)) return true;
        var dot = host.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) return true; // luật ngầm "*"
        return _wildcards.Contains(host[(dot + 1)..]);
    }

    private static string ToAscii(string rule) => Idn.GetAscii(rule).ToLowerInvariant();

    private static PublicSuffixList LoadEmbedded()
    {
        using var stream = typeof(PublicSuffixList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Thiếu public_suffix_list.dat nhúng trong LunaDns.Core.");
        using var reader = new StreamReader(stream);
        return Parse(reader);
    }
}
