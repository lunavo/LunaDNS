using LunaDns.Core.Dns;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Probing;

/// <summary>Một tổ hợp cắt gói D3 thử: kiểu cắt (D1) và độ trễ giữa hai phần.</summary>
public sealed record TuneCandidate(HelloSplit Split, int DelayMs)
{
    public static TuneCandidate Default { get; } = new(HelloSplit.Default, 0);

    public TimeSpan Delay => TimeSpan.FromMilliseconds(DelayMs);
}

/// <summary>Cấu hình đang dùng ở nơi giữ thật (Windows: settings của app; macOS: daemon).</summary>
public sealed record TuningState(DohProvider Doh, TuneCandidate Candidate);

/// <summary>Thứ tự thử của D3: tổ hợp đang dùng trước, rồi đi từ ít can thiệp tới nhiều.</summary>
public static class TuneCandidates
{
    public const int MaxCount = 8;

    public static IReadOnlyList<TuneCandidate> Table { get; } =
    [
        new(new HelloSplit(SplitMode.FirstByte, Records: false), 0),
        new(new HelloSplit(SplitMode.FirstByte, Records: false), 10),
        new(new HelloSplit(SplitMode.MidSni, Records: false), 0),
        new(new HelloSplit(SplitMode.MidSni, Records: false), 10),
        new(new HelloSplit(SplitMode.FirstByte, Records: true), 0),
        new(new HelloSplit(SplitMode.FirstByte, Records: true), 10),
        new(new HelloSplit(SplitMode.MidSni, Records: true), 0),
        new(new HelloSplit(SplitMode.MidSni, Records: true), 10),
    ];

    public static IReadOnlyList<TuneCandidate> Ordered(TuneCandidate current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return [.. Table.Where(c => c != current).Prepend(current).Take(MaxCount)];
    }
}
