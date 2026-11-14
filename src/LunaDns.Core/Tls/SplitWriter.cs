namespace LunaDns.Core.Tls;

/// <summary>
/// Ghi ClientHello thành nhiều lần Write theo <see cref="HelloSplitter"/>. Với socket bật NoDelay, mỗi lần Write
/// thành một TCP segment riêng, nên bộ lọc SNI không thấy trọn tên host trong một gói (spec 4.3).
/// </summary>
public static class SplitWriter
{
    /// <summary>Độ trễ lớn nhất giữa hai mảnh (mục Nâng cao, spec 4.3).</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(20);

    public static async Task WriteAsync(Stream destination, ReadOnlyMemory<byte> hello, SplitMode mode, TimeSpan delay, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(delay, MaxDelay);

        var pieces = HelloSplitter.Split(hello, mode);
        for (var i = 0; i < pieces.Count; i++)
        {
            if (i > 0 && delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await destination.WriteAsync(pieces[i], cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
