using LunaDns.Core.Tls;

namespace LunaDns.Core.Features;

/// <summary>Tuỳ chọn cắt gói từ mục Nâng cao (spec 4.3, 4.8, D1). Áp dụng khi dịch vụ được mở (bật hoặc watchdog mở lại).</summary>
public sealed record FeatureTuning(HelloSplit Split, TimeSpan SplitDelay)
{
    public static FeatureTuning Default { get; } = new(HelloSplit.Default, TimeSpan.Zero);
}
