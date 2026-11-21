using LunaDns.Core.Probing;
using LunaDns.Core.Tests.Tls;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public class SplittingStreamTests
{
    private static readonly byte[] Hello = TestClientHello.Build("store.steampowered.com");
    private static readonly byte[] Later = [9, 8, 7];

    [Fact]
    public async Task Splits_only_the_first_async_write()
    {
        var inner = new RecordingStream();
        await using var stream = new SplittingStream(inner, HelloSplit.Default);

        await stream.WriteAsync(Hello);
        await stream.WriteAsync(Later);

        var writes = inner.Writes;
        Assert.Equal(3, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello.Length - 1, writes[1].Length);
        Assert.Equal(Later, writes[2]);
    }

    [Fact]
    public void Splits_only_the_first_sync_write()
    {
        var inner = new RecordingStream();
        using var stream = new SplittingStream(inner, HelloSplit.Default);

        stream.Write(Hello, 0, Hello.Length);
        stream.Write(Later, 0, Later.Length);

        var writes = inner.Writes;
        Assert.Equal(3, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello, writes[0].Concat(writes[1]).ToArray());
    }

    [Fact]
    public async Task Records_mode_splits_only_the_first_record_of_the_first_write()
    {
        var inner = new RecordingStream();
        await using var stream = new SplittingStream(inner, new HelloSplit(SplitMode.FirstByte, Records: true));

        await stream.WriteAsync((byte[])[.. Hello, .. Later]);

        var writes = inner.Writes;
        Assert.Equal(3, writes.Count);
        Assert.Equal(11, writes[0].Length);
        Assert.Equal(Hello.Length - 6, writes[1].Length); // 5 byte header mới + phần thân còn lại
        Assert.Equal(Later, writes[2]);
    }

    [Fact]
    public async Task Delay_is_applied_between_pieces_of_the_first_write()
    {
        var inner = new RecordingStream();
        await using var stream = new SplittingStream(inner, HelloSplit.Default, TimeSpan.FromMilliseconds(20));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await stream.WriteAsync(Hello);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(15), $"chỉ nghỉ {watch.Elapsed.TotalMilliseconds} ms");
        var writes = inner.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(Hello, writes[0].Concat(writes[1]).ToArray());
    }

    [Fact]
    public void Sync_write_also_applies_delay()
    {
        var inner = new RecordingStream();
        using var stream = new SplittingStream(inner, HelloSplit.Default, TimeSpan.FromMilliseconds(20));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        stream.Write(Hello, 0, Hello.Length);

        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(15));
        Assert.Equal(2, inner.Writes.Count);
    }
}
