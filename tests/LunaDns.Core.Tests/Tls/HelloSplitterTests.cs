using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class HelloSplitterTests
{
    private static readonly int[] FirstCut = [1];

    [Fact]
    public void FirstByte_cuts_after_first_byte()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        Assert.Equal(FirstCut, HelloSplitter.CutPoints(hello, SplitMode.FirstByte));
    }

    [Fact]
    public void MidSni_cuts_inside_host_name()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        ClientHelloParser.TryFindSni(hello, out var sni);

        var cut = Assert.Single(HelloSplitter.CutPoints(hello, SplitMode.MidSni));
        Assert.InRange(cut, sni.Offset + 1, sni.Offset + sni.Length - 1);
    }

    [Fact]
    public void MidSni_falls_back_to_first_byte_without_sni()
    {
        var hello = TestClientHello.Build(null, paddingLength: 8);
        Assert.Equal(FirstCut, HelloSplitter.CutPoints(hello, SplitMode.MidSni));
    }

    [Fact]
    public void One_byte_input_has_no_cut() =>
        Assert.Empty(HelloSplitter.CutPoints([0x16], SplitMode.FirstByte));

    [Theory]
    [InlineData(SplitMode.FirstByte)]
    [InlineData(SplitMode.MidSni)]
    public void Pieces_concatenate_to_original(SplitMode mode)
    {
        var hello = TestClientHello.Build("steamcommunity.com", paddingLength: 100);

        var pieces = HelloSplitter.Split(hello, mode);

        Assert.Equal(2, pieces.Count);
        Assert.Equal(hello, pieces.SelectMany(p => p.ToArray()).ToArray());
    }
}
