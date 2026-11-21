using LunaDns.Core.Dns;

namespace LunaDns.Core.Probing;

public enum TuneOutcome
{
    /// <summary>Tìm được tổ hợp cắt gói cho cả 4 host.</summary>
    Found,
    /// <summary>Không host nào bị lọc SNI hay không vào được: chỉ cần DoH (và Định tuyến DNS nếu bị đầu độc).</summary>
    SplitNotNeeded,
    /// <summary>Không máy DoH nào trả lời.</summary>
    DohFailed,
    /// <summary>Không tổ hợp nào qua đủ 4 host.</summary>
    NothingWorked,
}

public enum TuneStage { MeasuringDoh, Classifying, TryingSplit }

/// <summary>Index/Total chỉ có nghĩa ở TryingSplit (1-based).</summary>
public sealed record TuneProgress(TuneStage Stage, int Index, int Total, TuneCandidate? Candidate);

/// <param name="Doh">Máy DoH đã chọn; null khi DohFailed.</param>
/// <param name="Candidate">Tổ hợp để áp: tìm được (Found) hoặc đang dùng (SplitNotNeeded); null khi DohFailed/NothingWorked.</param>
/// <param name="Probe">Kết quả bước phân loại, cho khối kết quả Kiểm tra; rỗng khi DohFailed.</param>
/// <param name="FailingHosts">NothingWorked: host còn lỗi ở tổ hợp qua được nhiều host nhất.</param>
public sealed record TuneResult(
    TuneOutcome Outcome,
    DohProvider? Doh,
    TuneCandidate? Candidate,
    ProbeRemedies Remedies,
    IReadOnlyList<ProbeResult> Probe,
    IReadOnlyList<string> FailingHosts);
