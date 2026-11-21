using LunaDns.Core.Features;

namespace LunaDns.Core.Domains;

/// <summary>Kết quả xác minh hoặc áp một file lớp 2 (spec D4). Tên dạng chuỗi đi qua socket daemon.</summary>
public enum DomainListOutcome
{
    Accepted,
    /// <summary>Cùng serial với bản đang giữ: lượt kiểm bình thường.</summary>
    NotNewer,
    Downgrade,
    TooLarge,
    Malformed,
    UnknownKey,
    BadSignature,
    UnknownFormat,
    /// <summary>Công tắc "Tự cập nhật danh sách" đang tắt.</summary>
    Disabled,
    ReapplyFailed,
    SaveFailed,
    /// <summary>Phía app: dịch vụ nền không trả lời hoặc là bản cũ.</summary>
    Unavailable,
}

/// <summary>Bản lớp 2 đã qua xác minh. <see cref="File"/> là nguyên byte đã tải, để lưu và xác minh lại khi nạp.</summary>
public sealed record DomainListUpdate(
    long Serial,
    DateTimeOffset Issued,
    IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> Groups,
    byte[] File);

public sealed record DomainListVerification(DomainListOutcome Outcome, DomainListUpdate? Update = null, int Dropped = 0);

/// <summary>Trạng thái hiển thị: công tắc, bản đang dùng, lần kiểm thành công gần nhất, tên miền lớp 2 đang có hiệu lực.</summary>
public sealed record DomainListState(
    bool Enabled,
    long Serial,
    DateTimeOffset? Issued,
    DateTimeOffset? CheckedAt,
    IReadOnlyDictionary<DomainGroup, IReadOnlyList<string>> Added)
{
    public static DomainListState Empty(bool enabled) => new(enabled, 0, null, null, new Dictionary<DomainGroup, IReadOnlyList<string>>());
}

/// <summary>Kết quả OfferAsync/SetEnabledAsync. Added: số tên miền lớp 2 mới có hiệu lực so với trước.</summary>
public sealed record DomainListOfferResult(
    DomainListOutcome Outcome,
    int Added = 0,
    int Dropped = 0,
    FeatureError Error = FeatureError.None,
    bool PreviousRestored = true)
{
    public bool Success => Outcome is DomainListOutcome.Accepted or DomainListOutcome.NotNewer;
}
