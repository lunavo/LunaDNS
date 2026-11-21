namespace LunaDns.Core.Domains;

public sealed record CustomDomain(string Domain, bool Enabled = true);

/// <summary>Lỗi về hình dạng của DomainSelection đến từ JSON ngoài (danh sách null, nhóm lạ hoặc trùng).</summary>
public enum DomainSelectionDefect
{
    None,
    MissingGroups,
    MissingCustom,
    UnknownGroup,
    DuplicateGroup,
}

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

    // JSON (cả settings.json sửa tay lẫn yêu cầu socket) có thể gán null cho danh sách hay phần tử dù kiểu không cho phép.
#pragma warning disable CA1508

    /// <summary>
    /// Dùng khi đọc file: danh sách null thành mặc định; bỏ nhóm lạ, dòng null, dòng không qua DomainPolicy, dòng trùng
    /// và phần vượt giới hạn. <paramref name="dropped"/> đếm cả nhóm lạ lẫn dòng bị bỏ.
    /// </summary>
    public DomainSelection Sanitized(out int dropped)
    {
        var defaults = new DomainSelection();
        var groups = EnabledGroups ?? defaults.EnabledGroups;
        var custom = Custom ?? defaults.Custom;
        var knownGroups = groups.Where(Enum.IsDefined).Distinct().ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<CustomDomain>();
        foreach (var entry in custom)
        {
            var domain = entry?.Domain is null ? null : DomainPolicy.Normalize(entry.Domain);
            if (domain is null || DomainPolicy.Validate(domain) != DomainRejection.None || !seen.Add(domain)) continue;
            if (kept.Count == DomainPolicy.MaxCustomDomains) break;
            kept.Add(entry! with { Domain = domain });
        }
        dropped = custom.Count - kept.Count + groups.Count(g => !Enum.IsDefined(g));
        return this with { EnabledGroups = knownGroups, Custom = kept };
    }

    /// <summary>Kiểm hình dạng cho dữ liệu đến từ ngoài; gọi trước <see cref="FirstInvalidIndex"/>.</summary>
    public DomainSelectionDefect CheckShape()
    {
        if (EnabledGroups is null) return DomainSelectionDefect.MissingGroups;
        if (Custom is null) return DomainSelectionDefect.MissingCustom;
        if (!EnabledGroups.All(Enum.IsDefined)) return DomainSelectionDefect.UnknownGroup;
        if (EnabledGroups.Distinct().Count() != EnabledGroups.Count) return DomainSelectionDefect.DuplicateGroup;
        return DomainSelectionDefect.None;
    }

    /// <summary>Kiểm nghiêm ngặt dòng Tuỳ chỉnh cho dữ liệu đến từ ngoài (dịch vụ nền macOS): null = hợp lệ.</summary>
    public int? FirstInvalidIndex()
    {
        if (Custom is null) return 0;
        if (Custom.Count > DomainPolicy.MaxCustomDomains) return DomainPolicy.MaxCustomDomains;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Custom.Count; i++)
        {
            var domain = Custom[i]?.Domain;
            if (domain is null || DomainPolicy.Normalize(domain) != domain
                || DomainPolicy.Validate(domain) != DomainRejection.None || !seen.Add(domain))
                return i;
        }
        return null;
    }

#pragma warning restore CA1508

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
