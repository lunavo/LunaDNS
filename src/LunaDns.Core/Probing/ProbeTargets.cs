namespace LunaDns.Core.Probing;

/// <summary>Host mẫu để kiểm tra (spec 4.1).</summary>
public static class ProbeTargets
{
    public static IReadOnlyList<string> Steam { get; } =
    [
        "store.steampowered.com", "steamcommunity.com", "api.steampowered.com", "cdn.akamai.steamstatic.com",
    ];

    public static IReadOnlyList<string> Pubg { get; } = ["prod-live-front.playbattlegrounds.com"];

    public static IReadOnlyList<string> For(bool includePubg)
    {
        if (!includePubg) return Steam;
        return [.. Steam, .. Pubg];
    }
}
