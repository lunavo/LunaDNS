using LunaDns.Core.Domains;

namespace LunaDns.Core.Probing;

/// <summary>Host mẫu để kiểm tra (spec v1 4.1, spec KH8 mục 6).</summary>
public static class ProbeTargets
{
    public static IReadOnlyList<string> Steam { get; } =
    [
        "store.steampowered.com", "steamcommunity.com", "api.steampowered.com", "cdn.akamai.steamstatic.com",
    ];

    public static IReadOnlyList<string> Pubg { get; } = ["prod-live-front.playbattlegrounds.com"];

    private static readonly string[] OtherGames = ["store.epicgames.com", "eu.battle.net", "www.ea.com"];

    private static readonly string[] Reddit = ["www.reddit.com", "i.redd.it"];

    public static IReadOnlyList<string> For(bool includePubg)
    {
        if (!includePubg) return Steam;
        return [.. Steam, .. Pubg];
    }

    public const int MaxCustomTargets = 5;

    public static IReadOnlyList<string> For(DomainSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var targets = DomainCatalog.Selectable.Where(selection.IsEnabled).SelectMany(For).ToList();
        if (selection.IsEnabled(DomainGroup.Custom))
            targets.AddRange(selection.Custom.Where(c => c.Enabled).Take(MaxCustomTargets).Select(c => c.Domain));
        return targets;
    }

    public static DomainGroup GroupOf(string host, DomainSelection selection) => GroupOf(host, selection, EffectiveCatalog.Builtin);

    public static DomainGroup GroupOf(string host, DomainSelection selection, EffectiveCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(catalog);
        foreach (var group in DomainCatalog.Selectable)
            if (DomainMatcher.Matches(host, catalog.For(group))) return group;
        if (DomainMatcher.Matches(host, DomainCatalog.For(DomainGroup.Pubg))) return DomainGroup.Pubg;
        return DomainGroup.Custom;
    }

    public static IReadOnlyList<string> For(DomainGroup group) => group switch
    {
        DomainGroup.Steam => Steam,
        DomainGroup.Pubg => Pubg,
        DomainGroup.OtherGames => OtherGames,
        DomainGroup.Reddit => Reddit,
        DomainGroup.Custom => [],
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };
}
