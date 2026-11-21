namespace LunaDns.Core.Domains;

public enum CatalogSource
{
    Builtin,
    Update,
}

public sealed record CatalogEntry(string Domain, CatalogSource Source);

/// <summary>
/// Nhóm dựng sẵn sau khi gộp lớp 1 (DomainCatalog) với lớp 2 (spec D4 "Gộp ba lớp"). Bất biến.
/// Chỉ nhóm trong DomainCatalog.Selectable nhận lớp 2; PUBG không bao giờ. Lọc lại mọi dòng (DomainPolicy, trùng lớp 1)
/// vì app macOS dựng catalog từ dữ liệu daemon gửi sang.
/// </summary>
public sealed class EffectiveCatalog
{
    private readonly Dictionary<DomainGroup, IReadOnlyList<string>> _added;

    private EffectiveCatalog(Dictionary<DomainGroup, IReadOnlyList<string>> added, DateTimeOffset? issued)
    {
        _added = added;
        Issued = issued;
    }

    public static EffectiveCatalog Builtin { get; } = new([], null);

    public DateTimeOffset? Issued { get; }

    public IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> AllAdded => _added;

    public static EffectiveCatalog From(IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>>? added, DateTimeOffset? issued)
    {
        if (added is null || added.Count == 0) return issued is null ? Builtin : new([], issued);
        var kept = new Dictionary<DomainGroup, IReadOnlyList<string>>();
        foreach (var group in DomainCatalog.Selectable)
        {
            if (!added.TryGetValue(group, out var list)) continue;
            var builtin = DomainCatalog.For(group);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var clean = list.Where(d => DomainPolicy.Normalize(d) == d
                    && DomainPolicy.Validate(d) == DomainRejection.None
                    && !DomainMatcher.Matches(d, builtin)
                    && seen.Add(d))
                .ToList();
            if (clean.Count > 0) kept[group] = clean;
        }
        return new(kept, issued);
    }

    public IReadOnlyList<string> Added(DomainGroup group) => _added.TryGetValue(group, out var list) ? list : [];

    public IReadOnlyList<string> For(DomainGroup group) => [.. DomainCatalog.For(group), .. Added(group)];

    public IReadOnlyList<CatalogEntry> Entries(DomainGroup group) =>
    [
        .. DomainCatalog.For(group).Select(d => new CatalogEntry(d, CatalogSource.Builtin)),
        .. Added(group).Select(d => new CatalogEntry(d, CatalogSource.Update)),
    ];
}
