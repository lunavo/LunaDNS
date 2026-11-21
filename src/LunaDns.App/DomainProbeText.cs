using LunaDns.App.ViewModels;
using LunaDns.Core.Probing;

namespace LunaDns.App;

/// <summary>Kết luận kiểm tra cho một tên miền (spec KH8 5.3).</summary>
internal static class DomainProbeText
{
    public static string Failed => Strings.DomainProbeFailed;

    public static string For(ProbeVerdict verdict) => verdict switch
    {
        ProbeVerdict.Ok => Strings.DomainProbeOk,
        ProbeVerdict.DnsPoisoned => Strings.DomainProbeNeedsDns,
        ProbeVerdict.SniFiltered => Strings.DomainProbeNeedsSni,
        ProbeVerdict.Unreachable => Strings.DomainProbeUnreachable,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, null),
    };

    public static Tone ToneFor(ProbeVerdict verdict) => verdict switch
    {
        ProbeVerdict.Ok => Tone.Warning,
        ProbeVerdict.Unreachable => Tone.Error,
        _ => Tone.Good,
    };
}
