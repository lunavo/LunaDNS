namespace LunaDns.Core.Features;

/// <summary>Ba công tắc trên giao diện (spec 4.8).</summary>
public enum Feature
{
    DnsRouting,
    SniProxy,
    PubgMode,
}

/// <summary>Tên tính năng ghi trong journal.json. Không đổi giá trị: mục cũ trên máy người dùng phải hoàn tác được.</summary>
public static class FeatureKeys
{
    public const string DnsRouting = "dns-routing";
    public const string SniProxy = "sni-proxy";
    public const string PubgMode = "pubg-mode";

    public static string For(Feature feature) => feature switch
    {
        Feature.DnsRouting => DnsRouting,
        Feature.SniProxy => SniProxy,
        Feature.PubgMode => PubgMode,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };
}
