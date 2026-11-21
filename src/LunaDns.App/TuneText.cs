using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tls;

namespace LunaDns.App;

/// <summary>Câu hiển thị cho lượt dò D3 "Tìm cấu hình hợp mạng".</summary>
internal static class TuneText
{
    public static string Candidate(TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return Strings.Format(nameof(Strings.TuneCandidateFormat),
            candidate.Split.Mode == SplitMode.MidSni ? Strings.TuneSplitMidSni : Strings.TuneSplitFirstByte,
            candidate.Split.Records ? Strings.TuneRecordsOn : Strings.TuneRecordsOff,
            candidate.DelayMs);
    }

    public static string Chosen(DohProvider doh, TuneCandidate candidate) =>
        Strings.Format(nameof(Strings.TuneChosenFormat), DohName(doh), Candidate(candidate));

    public static string Progress(TuneProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return progress.Stage switch
        {
            TuneStage.MeasuringDoh => Strings.TuneMeasuringDoh,
            TuneStage.Classifying => Strings.TuneClassifying,
            _ => Strings.Format(nameof(Strings.TuneTryingFormat), progress.Index, progress.Total,
                progress.Candidate is { } c ? Candidate(c) : string.Empty),
        };
    }

    public static string DohName(DohProvider provider) =>
        provider == DohProvider.Custom ? Strings.DohCustom : DohCatalog.Get(provider).Name;
}
