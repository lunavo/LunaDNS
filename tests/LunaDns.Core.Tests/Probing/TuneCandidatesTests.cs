using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public class TuneCandidatesTests
{
    private static TuneCandidate C(SplitMode mode, bool records, int delay) => new(new HelloSplit(mode, records), delay);

    [Fact]
    public void Table_goes_from_least_to_most_intrusive()
    {
        TuneCandidate[] expected =
        [
            C(SplitMode.FirstByte, false, 0), C(SplitMode.FirstByte, false, 10),
            C(SplitMode.MidSni, false, 0), C(SplitMode.MidSni, false, 10),
            C(SplitMode.FirstByte, true, 0), C(SplitMode.FirstByte, true, 10),
            C(SplitMode.MidSni, true, 0), C(SplitMode.MidSni, true, 10),
        ];

        Assert.Equal(expected, TuneCandidates.Table);
    }

    [Fact]
    public void Current_in_table_goes_first_and_is_not_repeated()
    {
        var current = C(SplitMode.MidSni, true, 0);

        var ordered = TuneCandidates.Ordered(current);

        Assert.Equal(8, ordered.Count);
        Assert.Equal(current, ordered[0]);
        Assert.Single(ordered, c => c == current);
        Assert.Equal(TuneCandidates.Table.Where(c => c != current), ordered.Skip(1));
    }

    [Fact]
    public void Current_outside_table_goes_first_then_first_seven_rows()
    {
        var current = C(SplitMode.FirstByte, false, 5);

        var ordered = TuneCandidates.Ordered(current);

        Assert.Equal(TuneCandidates.MaxCount, ordered.Count);
        Assert.Equal(current, ordered[0]);
        Assert.Equal(TuneCandidates.Table.Take(7), ordered.Skip(1));
    }

    [Fact]
    public void Default_is_first_row() => Assert.Equal(TuneCandidates.Table[0], TuneCandidate.Default);
}
