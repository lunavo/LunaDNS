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
        Assert.Equal(6, writes[0].Length);
        Assert.Equal(Hello.Length - 1, writes[1].Length); // 5 byte header mới + phần thân còn lại
        Assert.Equal(Later, writes[2]);
    }
}
