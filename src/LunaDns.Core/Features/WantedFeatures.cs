namespace LunaDns.Core.Features;

/// <summary>
/// F1: những tính năng người dùng để bật lần cuối bằng lệnh tường minh. Chế độ PUBG không bao giờ tự bật nên không có ở đây.
/// Chỉ có hàm, không có thuộc tính tính toán, để settings.json chỉ ghi hai trường.
/// </summary>
public sealed record WantedFeatures(bool DnsRouting = false, bool SniProxy = false)
{
    public static WantedFeatures None { get; } = new();

    public bool IsEmpty() => !DnsRouting && !SniProxy;

    /// <summary>Theo thứ tự bật lại: Định tuyến DNS trước, Proxy SNI sau.</summary>
    public IReadOnlyList<Feature> List()
    {
        var list = new List<Feature>(2);
        if (DnsRouting) list.Add(Feature.DnsRouting);
        if (SniProxy) list.Add(Feature.SniProxy);
        return list;
    }

    public bool Contains(Feature feature) => feature switch
    {
        Feature.DnsRouting => DnsRouting,
        Feature.SniProxy => SniProxy,
        _ => false,
    };

    public WantedFeatures With(Feature feature, bool on) => feature switch
    {
        Feature.DnsRouting => this with { DnsRouting = on },
        Feature.SniProxy => this with { SniProxy = on },
        _ => this,
    };

    /// <summary>Phần muốn bật mà đang không chạy.</summary>
    public WantedFeatures Missing(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new(DnsRouting && !status.DnsRouting, SniProxy && !status.SniProxy);
    }

    public WantedFeatures Union(WantedFeatures other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(DnsRouting || other.DnsRouting, SniProxy || other.SniProxy);
    }

    public static WantedFeatures From(FeatureStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return new(status.DnsRouting, status.SniProxy);
    }
}

/// <summary>
/// Ảnh chụp F1 cho giao diện: công tắc Tự bật lại, wanted hiện tại, đang tự bật dở hay không, và lỗi của lần tự bật cuối
/// (null nếu không lỗi hoặc người dùng đã ra lệnh cho tính năng đó).
/// </summary>
public sealed record ResumeState(bool Enabled, WantedFeatures Wanted, bool Restoring, FeatureResult? Failure);
