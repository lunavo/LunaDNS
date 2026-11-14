using LunaDns.Core.Tls;

namespace LunaDns.Core.Proxy;

/// <summary>Cấu hình listener 127.0.0.1:443 của Chế độ PUBG (spec 4.4).</summary>
public sealed record SniForwarderOptions
{
    /// <summary>Chỉ chuyển tiếp ClientHello có SNI thuộc các domain này; SNI khác hoặc không có SNI thì đóng.</summary>
    public required IReadOnlyList<string> Domains { get; init; }

    public SplitMode SplitMode { get; init; } = SplitMode.FirstByte;

    public TimeSpan SplitDelay { get; init; } = TimeSpan.Zero;

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Domains);
        if (SplitDelay < TimeSpan.Zero || SplitDelay > SplitWriter.MaxDelay)
            throw new ArgumentException("Độ trễ cắt gói phải nằm trong khoảng 0–20 ms.");
    }
}
