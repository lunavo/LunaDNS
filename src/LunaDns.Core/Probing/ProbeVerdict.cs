namespace LunaDns.Core.Probing;

/// <summary>Kết luận cho một host (bảng ở spec 4.1).</summary>
public enum ProbeVerdict
{
    /// <summary>DNS hệ thống đúng và TLS nguyên vẹn thành công.</summary>
    Ok,
    /// <summary>DNS hệ thống trả IP sai (hoặc không trả), còn TLS tới IP của DoH thì thành công.</summary>
    DnsPoisoned,
    /// <summary>TLS nguyên vẹn lỗi, TLS cắt gói thành công.</summary>
    SniFiltered,
    /// <summary>Cả hai cách đều lỗi, hoặc DoH không cho được IP nào.</summary>
    Unreachable,
}
