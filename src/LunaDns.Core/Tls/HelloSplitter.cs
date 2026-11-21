namespace LunaDns.Core.Tls;

/// <summary>
/// Cắt ClientHello theo <see cref="HelloSplit"/>. Records tắt: các mảnh của một record (cắt TCP, spec 4.3).
/// Records bật: hai TLS record hoàn chỉnh (D1); điểm cắt luôn nằm trong phần thân.
/// </summary>
public static class HelloSplitter
{
    public static int[] CutPoints(ReadOnlySpan<byte> hello, HelloSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var cut = split.Records ? TlsRecordSplitter.HeaderLength + 1 : 1;
        if (split.Mode == SplitMode.MidSni && ClientHelloParser.TryFindSni(hello, out var sni) && sni.Length > 1)
            cut = sni.Offset + sni.Length / 2;
        return cut > 0 && cut < hello.Length ? [cut] : [];
    }

    public static IReadOnlyList<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> hello, HelloSplit split)
    {
        ArgumentNullException.ThrowIfNull(split);
        var cuts = CutPoints(hello.Span, split);
        if (split.Records)
        {
            // Chỉ tách khi có trọn một record; thiếu byte (client đóng sớm) thì gửi nguyên.
            return cuts.Length == 1 && ClientHelloParser.RecordLength(hello.Span) == hello.Length
                ? TlsRecordSplitter.Split(hello, cuts[0])
                : [hello];
        }

        var pieces = new List<ReadOnlyMemory<byte>>();
        var start = 0;
        foreach (var cut in cuts)
        {
            pieces.Add(hello[start..cut]);
            start = cut;
        }
        pieces.Add(hello[start..]);
        return pieces;
    }
}
