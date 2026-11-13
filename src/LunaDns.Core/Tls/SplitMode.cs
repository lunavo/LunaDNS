namespace LunaDns.Core.Tls;

public enum SplitMode
{
    /// <summary>Cắt sau byte đầu tiên của record (mặc định, spec 4.3).</summary>
    FirstByte,
    /// <summary>Cắt giữa tên host trong SNI; không có SNI thì dùng FirstByte.</summary>
    MidSni,
}
