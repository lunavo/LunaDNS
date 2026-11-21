namespace LunaDns.Core.Domains;

/// <summary>
/// Nhóm tên miền dựng sẵn (spec KH8 mục 6). Không bao giờ chứa domain anti-cheat (spec v1 4.4, DomainDenyList).
/// Task 13 (thử thật VNPT 21/11/2026, docs/spikes/2026-11-preset-groups-field-test.md): OtherGames không bị chặn nên
/// bỏ khỏi <see cref="Selectable"/> theo quyết định PO; code nhóm giữ lại để thử lại sau.
/// </summary>
public static class DomainCatalog
{
    private static readonly string[] Steam =
    [
        "steampowered.com", "steamcommunity.com", "steamstatic.com", "steam-chat.com",
        "steamusercontent.com", "steamgames.com", "steamcontent.com", "steamserver.net",
        "counter-strike.net", "dota2.com", "valvesoftware.com",
    ];

    // Chỉ là lobby/web; *xenuine* và accounts.pubg.com cố ý không có (spec v1 4.4).
    private static readonly string[] Pubg =
    [
        "playbattlegrounds.com", "pubg.com", "krafton.com", "pubgapp.com", "pubgesports.com",
    ];

    // Gộp nhóm Other cũ với store/launcher. Không có tên miền anti-cheat; xem DomainDenyList.
    private static readonly string[] OtherGames =
    [
        "narakathegame.com", "24entertainment.net", "playdeltaforce.com", "levelinfinite.com",
        "epicgames.com", "epicgames.dev", "unrealengine.com",
        "battle.net", "blizzard.com",
        // Không định tuyến cả ea.com: ac.ea.com (EA Javelin) bị cấm, DomainDenyList khớp cả tên cha.
        // Tên con lấy từ thử thật www.ea.com 21/11/2026 (VNPT); EA app chưa thử.
        "www.ea.com", "drop-assets.ea.com",
    ];

    private static readonly string[] Reddit =
    [
        "reddit.com", "redd.it", "redditmedia.com", "redditstatic.com", "reddituploads.com",
    ];

    public static IReadOnlyList<DomainGroup> Selectable { get; } = [DomainGroup.Steam, DomainGroup.Reddit];

    public static IReadOnlyList<string> For(DomainGroup group) => group switch
    {
        DomainGroup.Steam => Steam,
        DomainGroup.Pubg => Pubg,
        DomainGroup.OtherGames => OtherGames,
        DomainGroup.Reddit => Reddit,
        DomainGroup.Custom => [],
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, null),
    };

    public static IReadOnlyList<string> All { get; } = [.. Steam.Concat(Pubg).Concat(OtherGames).Concat(Reddit).Distinct()];
}
