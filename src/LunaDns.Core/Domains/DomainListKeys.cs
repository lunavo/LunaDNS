namespace LunaDns.Core.Domains;

/// <summary>
/// Khoá công khai ký danh sách lớp 2 (spec D4): keyId → SubjectPublicKeyInfo DER base64. Khoá bí mật chỉ ở máy PO.
/// Đổi khoá: thêm khoá mới ở một bản app, ký bằng khoá mới từ bản sau. Rỗng thì mọi file ra UnknownKey (chạy với lớp 1).
/// </summary>
public static class DomainListKeys
{
    public static IReadOnlyDictionary<string, string> Production { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
