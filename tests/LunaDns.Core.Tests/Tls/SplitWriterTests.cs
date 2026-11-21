using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class SplitWriterTests
{
    private static readonly byte[] Hello = TestClientHello.Build("store.steampowered.com");

    [Fact]
    public async Task FirstByte_writes_one_byte_then_the_rest()
    {
        using var stream = new RecordingStream();

        await SplitWriter.WriteAsync(stream, Hello, new HelloSplit(SplitMode.FirstByte), TimeSpan.Zero, CancellationToken.None);

        var writes = stream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Single(writes[0]);
        Assert.Equal(Hello, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task MidSni_cuts_inside_the_host_name()
    {
        using var stream = new RecordingStream();
        Assert.True(ClientHelloParser.TryFindSni(Hello, out var sni));

        await SplitWriter.WriteAsync(stream, Hello, new HelloSplit(SplitMode.MidSni), TimeSpan.Zero, CancellationToken.None);

        var writes = stream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(sni.Offset + sni.Length / 2, writes[0].Length);
        Assert.Equal(Hello, writes.SelectMany(w => w).ToArray());
    }

    [Fact]
    public async Task Delay_between_pieces_keeps_bytes_intact()
    {
        using var stream = new RecordingStream();

        await SplitWriter.WriteAsync(stream, Hello, new HelloSplit(SplitMode.FirstByte), TimeSpan.FromMilliseconds(10), CancellationToken.None);

        Assert.Equal(Hello, stream.Writes.SelectMany(w => w).ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public async Task Rejects_delay_outside_0_to_20_ms(int milliseconds)
    {
        using var stream = new RecordingStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SplitWriter.WriteAsync(stream, Hello, new HelloSplit(SplitMode.FirstByte), TimeSpan.FromMilliseconds(milliseconds), CancellationToken.None));
    }

    [Fact]
    public async Task Records_mode_writes_two_records()
    {
        using var stream = new RecordingStream();

        await SplitWriter.WriteAsync(stream, Hello, new HelloSplit(SplitMode.FirstByte, Records: true), TimeSpan.Zero, CancellationToken.None);

        var writes = stream.Writes;
        Assert.Equal(2, writes.Count);
        Assert.Equal(6, writes[0].Length);
        Assert.Equal(Hello.Length + 5, writes.Sum(w => w.Length));
        Assert.All(writes, w => Assert.Equal(0x16, w[0]));
    }
}
