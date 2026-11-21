using System.Text.Json.Serialization;

namespace LunaDns.Core.Domains;

/// <summary>Vỏ của domains.json: payload là base64 của JSON UTF-8, sig ký trên đúng các byte đó.</summary>
internal sealed record DomainListEnvelope(string? KeyId, string? Payload, string? Sig);

internal sealed record DomainListPayload(int Format, long? Serial, DateTimeOffset? Issued, Dictionary<string, List<string>?>? Groups);

/// <summary>Nguồn lớp 2 trong repo (data/domain-list.json): chỉ có groups, serial/issued do lệnh sign đặt.</summary>
public sealed record DomainListSource(Dictionary<string, List<string>> Groups);

/// <summary>domain-list.json trong thư mục dữ liệu. Envelope là base64 của nguyên file đã tải.</summary>
internal sealed record DomainListFile(string? Envelope, long HighestSerial, DateTimeOffset? CheckedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false)]
[JsonSerializable(typeof(DomainListEnvelope))]
[JsonSerializable(typeof(DomainListPayload))]
[JsonSerializable(typeof(DomainListSource))]
[JsonSerializable(typeof(DomainListFile))]
internal sealed partial class DomainListJsonContext : JsonSerializerContext
{
}
