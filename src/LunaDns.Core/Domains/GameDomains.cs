namespace LunaDns.Core.Domains;

/// <summary>Domain được định tuyến DNS/proxy, theo nhóm. Không bao giờ chứa domain anti-cheat (spec 4.4).</summary>
public static class GameDomains
{
    private static readonly string[] Steam =
    [
        "steampowered.com", "steamcommunity.com", "steamstatic.com", "steam-chat.com",
        "steamusercontent.com", "steamgames.com", "steamcontent.com", "steamserver.net",
        "counter-strike.net", "dota2.com", "valvesoftware.com",
    ];

    // Chỉ là lobby/web; *xenuine* và accounts.pubg.com cố ý không có (spec 4.4).
    private static readonly string[] Pubg =
    [
        "playbattlegrounds.com", "pubg.com", "krafton.com", "pubgapp.com", "pubgesports.com",
    ];

    private static readonly string[] Other =
    [
        "narakathegame.com", "24entertainment.net", "playdeltaforce.com", "levelinfinite.com",
    ];

    public static IReadOnlyList<string> For(DomainGroup group) => group switch
    {
        DomainGroup.Steam => Steam,
        DomainGroup.Pubg => Pubg,
        DomainGroup.Other => Other,
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    public static IReadOnlyList<string> All { get; } = [.. Steam.Concat(Pubg).Concat(Other).Distinct()];
}
