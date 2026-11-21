// src/LunaDns.Core/Domains/DomainPolicy.cs
using System.Globalization;
using System.Net;

namespace LunaDns.Core.Domains;

public enum DomainRejection
{
    None,
    Syntax,
    PublicSuffix,
    LocalName,
    Denied,
    Duplicate,
    Limit,
}

/// <param name="Input">Chữ user nhập (để giữ lại khi lỗi).</param>
/// <param name="Domain">Tên đã chuẩn hoá, null nếu dòng trống hoặc chú thích.</param>
/// <param name="Detail">Denied: lý do. Duplicate: tên miền (hoặc tên cha) đã có.</param>
public sealed record DomainCheck(string Input, string? Domain, DomainRejection Rejection, string? Detail = null)
{
    public bool Ok => Rejection == DomainRejection.None && Domain is not null;
}

/// <summary>Chuẩn hoá và kiểm tra tên miền (spec KH8 mục 4). Dùng ở UI, settings, FeatureSet, router và dịch vụ nền macOS.</summary>
public static class DomainPolicy
{
    public const int MaxCustomDomains = 200;

    private static readonly IdnMapping Idn = new();
    private static readonly string[] LocalSuffixes = ["localhost", "local", "lan", "home.arpa", "internal", "arpa", "test", "invalid"];
    private static readonly char[] Separators = ['\n', '\r', ',', ' ', '\t', ';'];

    public static string? Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var s = raw.Trim();
        if (s.Length == 0 || s.StartsWith('#')) return null;
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];
        var cut = s.IndexOfAny(['/', '?', '#']);
        if (cut >= 0) s = s[..cut];
        var at = s.LastIndexOf('@');
        if (at >= 0) s = s[(at + 1)..];
        var colon = s.LastIndexOf(':');
        if (colon >= 0 && s.IndexOf(':', StringComparison.Ordinal) == colon) s = s[..colon]; // một dấu ":" = cổng; IPv6 để Validate từ chối
        if (s.StartsWith("*.", StringComparison.Ordinal)) s = s[2..];
        s = DomainMatcher.Normalize(s);
        if (s.StartsWith("www.", StringComparison.Ordinal)) s = s[4..];
        try
        {
            s = Idn.GetAscii(s);
        }
        catch (ArgumentException)
        {
            return s; // nhãn rỗng/ký tự cấm: Validate trả Syntax
        }
        return s.ToLowerInvariant();
    }

    public static DomainRejection Validate(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.Length is 0 or > 253) return DomainRejection.Syntax;
        if (IPAddress.TryParse(host, out _)) return DomainRejection.Syntax;
        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || label[0] == '-' || label[^1] == '-') return DomainRejection.Syntax;
            if (!label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')) return DomainRejection.Syntax;
        }
        if (labels.Length == 1 || LocalSuffixes.Any(s => host == s || host.EndsWith("." + s, StringComparison.Ordinal)))
            return DomainRejection.LocalName;
        if (PublicSuffixList.Default.IsPublicSuffix(host)) return DomainRejection.PublicSuffix;
        if (DomainDenyList.Find(host) is not null) return DomainRejection.Denied;
        return DomainRejection.None;
    }

    public static bool IsRoutable(string host) => Validate(DomainMatcher.Normalize(host)) == DomainRejection.None;

    /// <summary>Tách chữ dán vào thành từng mục. Bỏ phần sau "#" của mỗi dòng.</summary>
    public static IReadOnlyList<string> SplitInput(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var items = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var hash = line.IndexOf('#', StringComparison.Ordinal);
            var content = hash >= 0 ? line[..hash] : line;
            items.AddRange(content.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return items;
    }

    public static DomainCheck Check(string raw, IReadOnlyCollection<string> existingCustom, IReadOnlyList<string> enabledCatalog)
    {
        ArgumentNullException.ThrowIfNull(existingCustom);
        ArgumentNullException.ThrowIfNull(enabledCatalog);
        var domain = Normalize(raw);
        if (domain is null) return new DomainCheck(raw, null, DomainRejection.None);
        var rejection = Validate(domain);
        if (rejection == DomainRejection.Denied)
            return new DomainCheck(raw, domain, rejection, DomainDenyList.Find(domain)?.Reason);
        if (rejection != DomainRejection.None) return new DomainCheck(raw, domain, rejection);
        if (existingCustom.Contains(domain, StringComparer.Ordinal))
            return new DomainCheck(raw, domain, DomainRejection.Duplicate, domain);
        var parent = enabledCatalog.FirstOrDefault(c => DomainMatcher.Matches(domain, [c]));
        if (parent is not null) return new DomainCheck(raw, domain, DomainRejection.Duplicate, parent);
        if (existingCustom.Count >= MaxCustomDomains) return new DomainCheck(raw, domain, DomainRejection.Limit);
        return new DomainCheck(raw, domain, DomainRejection.None);
    }
}
