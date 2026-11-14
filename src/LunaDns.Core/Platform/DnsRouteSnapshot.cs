namespace LunaDns.Core.Platform;

/// <summary>
/// Luật định tuyến DNS sắp thêm. <see cref="Marker"/> được ghi vào Comment của luật và là duy nhất cho mỗi lần áp dụng;
/// hoàn tác chỉ gỡ luật mang đúng dấu này. <see cref="SkippedNamespaces"/> là các namespace bỏ qua vì đã có luật của phần mềm khác.
/// </summary>
public sealed record DnsRouteSnapshot(string Marker, IReadOnlyList<string> Namespaces, string NameServer, IReadOnlyList<string> SkippedNamespaces);
