using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.App.Tests;

public sealed class TuneTextTests
{
    private static readonly TuneCandidate MidSniRecords = new(new HelloSplit(SplitMode.MidSni, true), 10);

    [Fact]
    public void Candidate_names_cut_records_and_delay() =>
        Assert.Equal("giữa SNI · tách record · 10 ms", TuneText.Candidate(MidSniRecords));

    [Fact]
    public void Chosen_names_doh_and_candidate() =>
        Assert.Equal("Đã chọn Quad9 · sau byte đầu · một record · 0 ms", TuneText.Chosen(DohProvider.Quad9, TuneCandidate.Default));

    [Fact]
    public void Progress_per_stage()
    {
        Assert.Equal(Strings.TuneMeasuringDoh, TuneText.Progress(new TuneProgress(TuneStage.MeasuringDoh, 0, 0, null)));
        Assert.Equal(Strings.TuneClassifying, TuneText.Progress(new TuneProgress(TuneStage.Classifying, 0, 0, TuneCandidate.Default)));
        Assert.Equal("Đang thử 3/8: giữa SNI · tách record · 10 ms",
            TuneText.Progress(new TuneProgress(TuneStage.TryingSplit, 3, 8, MidSniRecords)));
    }
}
