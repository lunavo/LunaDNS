using LunaDns.Core.Dns;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Settings;

/// <summary>Tổ hợp đã chạy được trên một mạng. <c>Stale</c>: lần Kiểm tra sau trên mạng này bị chặn.</summary>
public sealed record NetworkProfile(
    string Id,
    bool Weak,
    string Label,
    DohProvider Doh,
    TuneCandidate Candidate,
    DateTimeOffset SavedAt,
    DateTimeOffset LastSeen,
    bool Stale = false);
