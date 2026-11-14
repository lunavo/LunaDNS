namespace LunaDns.Core.Platform;

/// <summary>
/// Cấu hình auto-proxy trước khi LunaDNS ghi <see cref="NewValue"/>.
/// Windows: AutoConfigURL của một người dùng (theo SID), <see cref="Services"/> là null.
/// macOS: <see cref="Services"/> giữ giá trị cũ của từng network service; các trường Windows để trống.
/// Trường thêm sau có giá trị mặc định để journal ghi bởi bản cũ vẫn đọc được.
/// </summary>
public sealed record AutoProxySnapshot(
    string UserSid,
    bool HadPreviousValue,
    string? PreviousValue,
    bool PreviousWasExpandString,
    string NewValue,
    IReadOnlyList<ServiceAutoProxy>? Services = null);

/// <summary>Auto-proxy của một network service macOS trước khi đổi: URL (null nếu chưa từng đặt) và trạng thái bật.</summary>
public sealed record ServiceAutoProxy(string Service, string? PreviousUrl, bool PreviousEnabled);
