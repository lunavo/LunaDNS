namespace LunaDns.Core.Domains;

/// <summary>
/// Khoá công khai ký danh sách lớp 2 (spec D4): keyId → SubjectPublicKeyInfo DER base64. Khoá bí mật chỉ ở máy PO.
/// Đổi khoá: thêm khoá mới ở một bản app, ký bằng khoá mới từ bản sau. Rỗng thì mọi file ra UnknownKey (chạy với lớp 1).
/// </summary>
public static class DomainListKeys
{
    public static IReadOnlyDictionary<string, string> Production { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 21/11/2026, khoá ~/.lunadns/domain-list-2026a.pem trên máy PO.
            ["2026a"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEjkC5PqIKIbdmWgZrZk1U8xImT96LNGSMPCu7fWIpd6Wy9vuGjposCHOqYGBVAteiXWcGdFt2EphsBYrgs8/8xA==",
        };
}
