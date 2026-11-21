namespace LunaDns.Core.Tls;

public static class HelloSplitter
{
    public static int[] CutPoints(ReadOnlySpan<byte> hello, HelloSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var cut = 1;
        if (split.Mode == SplitMode.MidSni && ClientHelloParser.TryFindSni(hello, out var sni) && sni.Length > 1)
            cut = sni.Offset + sni.Length / 2;
        return cut > 0 && cut < hello.Length ? [cut] : [];
    }

    public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> hello, HelloSplit split)
    {
        var pieces = new List<ReadOnlyMemory<byte>>();
        var start = 0;
        foreach (var cut in CutPoints(hello.Span, split))
        {
            pieces.Add(hello[start..cut]);
            start = cut;
        }
        pieces.Add(hello[start..]);
        return pieces;
    }
}
