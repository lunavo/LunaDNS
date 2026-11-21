namespace LunaDns.Core.Domains;

public sealed record CustomDomain(string Domain, bool Enabled = true);

/// <summary>Nhóm nào đang bật và danh sách Tuỳ chỉnh (spec KH8 3.1). Bất biến; đổi bằng with/WithGroup.</summary>
public sealed record DomainSelection
{
    public IReadOnlyList<DomainGroup> EnabledGroups { get; init; } = [DomainGroup.Steam, DomainGroup.Custom];

    public IReadOnlyList<CustomDomain> Custom { get; init; } = [];

    public bool IsEnabled(DomainGroup group) => EnabledGroups.Contains(group);

    public DomainSelection WithGroup(DomainGroup group, bool enabled) => this with
    {
        EnabledGroups = enabled
            ? [.. EnabledGroups.Where(g => g != group), group]
            : [.. EnabledGroups.Where(g => g != group)],
    };

    /// <summary>Dùng khi đọc file: bỏ dòng không qua DomainPolicy, dòng trùng và phần vượt giới hạn.</summary>
    public DomainSelection Sanitized(out int dropped)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<CustomDomain>();
        foreach (var entry in Custom)
        {
            var domain = entry.Domain is null ? null : DomainPolicy.Normalize(entry.Domain);
            if (domain is null || DomainPolicy.Validate(domain) != DomainRejection.None || !seen.Add(domain)) continue;
            if (kept.Count == DomainPolicy.MaxCustomDomains) break;
            kept.Add(entry with { Domain = domain });
        }
        dropped = Custom.Count - kept.Count;
        return this with { EnabledGroups = [.. EnabledGroups.Distinct()], Custom = kept };
    }

    /// <summary>Kiểm nghiêm ngặt cho dữ liệu đến từ ngoài (dịch vụ nền macOS): null = hợp lệ.</summary>
    public int? FirstInvalidIndex()
    {
        if (Custom.Count > DomainPolicy.MaxCustomDomains) return DomainPolicy.MaxCustomDomains;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Custom.Count; i++)
        {
            var domain = Custom[i].Domain;
            if (domain is null || DomainPolicy.Normalize(domain) != domain
                || DomainPolicy.Validate(domain) != DomainRejection.None || !seen.Add(domain))
                return i;
        }
        return null;
    }

    public bool SameAs(DomainSelection other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return EnabledGroups.OrderBy(g => g).SequenceEqual(other.EnabledGroups.OrderBy(g => g)) && Custom.SequenceEqual(other.Custom);
    }

    // Record mặc định so sánh danh sách theo tham chiếu, làm AppSettings (có Domains) không bằng nhau sau khi Save/Load.
    public bool Equals(DomainSelection? other) => other is not null && SameAs(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var g in EnabledGroups.OrderBy(g => g)) hash.Add(g);
        foreach (var c in Custom) hash.Add(c);
        return hash.ToHashCode();
    }
}
