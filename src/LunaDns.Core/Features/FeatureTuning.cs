using LunaDns.Core.Tls;

namespace LunaDns.Core.Features;

/// <summary>Tuỳ chọn cắt gói từ mục Nâng cao (spec 4.3, 4.8). Áp dụng khi dịch vụ được mở (bật hoặc watchdog mở lại).</summary>
public sealed record FeatureTuning(SplitMode SplitMode, TimeSpan SplitDelay)
{
    public static FeatureTuning Default { get; } = new(SplitMode.FirstByte, TimeSpan.Zero);
}
