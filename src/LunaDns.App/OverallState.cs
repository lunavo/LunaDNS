using LunaDns.App.ViewModels;
using LunaDns.Core.Features;

namespace LunaDns.App;

internal enum OverallKind
{
    Off,
    On,
    Busy,
    Attention,
}

/// <summary>
/// Trạng thái tổng cho huy hiệu header, icon khay và tooltip (KH6 3.2, 3.4). Ưu tiên Đang xử lý &gt; Cần xử lý &gt; Bật &gt; Tắt.
/// Chỉ đếm hai công tắc trên giao diện; Chế độ PUBG ẩn khỏi v1 (KH6 U0).
/// </summary>
internal sealed record OverallState(OverallKind Kind, IReadOnlyList<Feature> Active)
{
    public const int Total = 2;

    public static OverallState From(FeatureStatus status, bool busy, bool attention)
    {
        ArgumentNullException.ThrowIfNull(status);
        var active = new List<Feature>(Total);
        if (status.DnsRouting) active.Add(Feature.DnsRouting);
        if (status.SniProxy) active.Add(Feature.SniProxy);
        var kind = busy ? OverallKind.Busy
            : attention ? OverallKind.Attention
            : active.Count > 0 ? OverallKind.On
            : OverallKind.Off;
        return new OverallState(kind, active);
    }

    public Tone Tone => Kind switch
    {
        OverallKind.Off => Tone.Neutral,
        OverallKind.On => Tone.Good,
        OverallKind.Busy => Tone.Busy,
        _ => Tone.Error,
    };

    public string Text => Kind switch
    {
        OverallKind.Off => Strings.OverallOff,
        OverallKind.On => Strings.Format(nameof(Strings.OverallOnFormat), Active.Count, Total),
        OverallKind.Busy => Strings.OverallBusy,
        _ => Strings.OverallAttention,
    };

    public string Tooltip => Kind switch
    {
        OverallKind.Off => Strings.TrayTooltipOff,
        OverallKind.On => Strings.Format(nameof(Strings.TrayTooltipOnFormat), string.Join(", ", Active.Select(Strings.FeatureTitle))),
        OverallKind.Busy => Strings.TrayTooltipBusy,
        _ => Strings.TrayTooltipAttention,
    };
}
