using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class HelloSplitterTests
{
    private static readonly int[] FirstCut = [1];

    [Fact]
    public void FirstByte_cuts_after_first_byte()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        Assert.Equal(FirstCut, HelloSplitter.CutPoints(hello, new HelloSplit(SplitMode.FirstByte)));
    }

    [Fact]
    public void MidSni_cuts_inside_host_name()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        ClientHelloParser.TryFindSni(hello, out var sni);

        var cut = Assert.Single(HelloSplitter.CutPoints(hello, new HelloSplit(SplitMode.MidSni)));
        Assert.InRange(cut, sni.Offset + 1, sni.Offset + sni.Length - 1);
    }

    [Fact]
    public void MidSni_falls_back_to_first_byte_without_sni()
    {
        var hello = TestClientHello.Build(null, paddingLength: 8);
        Assert.Equal(FirstCut, HelloSplitter.CutPoints(hello, new HelloSplit(SplitMode.MidSni)));
    }

    [Fact]
    public void One_byte_input_has_no_cut() =>
        Assert.Empty(HelloSplitter.CutPoints([0x16], new HelloSplit(SplitMode.FirstByte)));

    [Theory]
    [InlineData(SplitMode.FirstByte)]
    [InlineData(SplitMode.MidSni)]
    public void Pieces_concatenate_to_original(SplitMode mode)
    {
        var hello = TestClientHello.Build("steamcommunity.com", paddingLength: 100);

        var pieces = HelloSplitter.Split(hello, new HelloSplit(mode));

        Assert.Equal(2, pieces.Count);
        Assert.Equal(hello, pieces.SelectMany(p => p.ToArray()).ToArray());
    }

    private static int BodyLength(ReadOnlyMemory<byte> record) => (record.Span[3] << 8) | record.Span[4];

    [Fact]
    public void Records_first_byte_puts_one_handshake_byte_in_first_record()
    {
        var hello = TestClientHello.Build("store.steampowered.com");

        var records = HelloSplitter.Split(hello, new HelloSplit(SplitMode.FirstByte, Records: true));

        Assert.Equal(2, records.Count);
        Assert.Equal(6, records[0].Length);
        Assert.Equal(1, BodyLength(records[0]));
        Assert.Equal(hello[5], records[0].Span[5]);
        Assert.Equal(hello.Length + 5, records.Sum(r => r.Length));
    }

    [Fact]
    public void Records_mid_sni_splits_host_name_across_records()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        Assert.True(ClientHelloParser.TryFindSni(hello, out var sni));

        var records = HelloSplitter.Split(hello, new HelloSplit(SplitMode.MidSni, Records: true));

        Assert.Equal(2, records.Count);
        Assert.Equal(sni.Offset + sni.Length / 2, records[0].Length);
        var host = System.Text.Encoding.ASCII.GetBytes(sni.Host);
        Assert.Equal(-1, records[0].Span.IndexOf(host));
        Assert.Equal(-1, records[1].Span.IndexOf(host));
    }

    [Fact]
    public void Records_mid_sni_without_sni_falls_back_to_first_byte()
    {
        var hello = TestClientHello.Build(null, paddingLength: 8);

        var records = HelloSplitter.Split(hello, new HelloSplit(SplitMode.MidSni, Records: true));

        Assert.Equal(6, records[0].Length);
    }

    [Fact]
    public void Records_with_one_byte_body_is_left_whole()
    {
        byte[] hello = [0x16, 0x03, 0x01, 0x00, 0x01, 0x01];

        var pieces = HelloSplitter.Split(hello, new HelloSplit(SplitMode.FirstByte, Records: true));

        Assert.Equal(hello, Assert.Single(pieces).ToArray());
    }

    [Fact]
    public void Records_with_incomplete_record_is_left_whole()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        var partial = hello.AsMemory(0, hello.Length - 3);

        var pieces = HelloSplitter.Split(partial, new HelloSplit(SplitMode.FirstByte, Records: true));

        Assert.Equal(partial.ToArray(), Assert.Single(pieces).ToArray());
    }
}
