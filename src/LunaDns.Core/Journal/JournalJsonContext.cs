using System.Text.Json.Serialization;
using LunaDns.Core.Platform;

namespace LunaDns.Core.Journal;

// Serializer sinh sẵn cho journal, để bản publish trimmed không cần JSON reflection (.NET 8 tắt reflection khi
// PublishTrimmed=true). Tuỳ chọn mặc định = đúng định dạng serializer reflection đã ghi (tên thuộc tính PascalCase),
// nên journal do bản cũ ghi vẫn đọc được (JournalFormatTests).

/// <summary>journal.json: danh sách mục, thụt dòng như bản cũ.</summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<JournalEntry>))]
[JsonSerializable(typeof(IReadOnlyList<JournalEntry>))]
internal sealed partial class JournalJsonContext : JsonSerializerContext;

/// <summary>Snapshot trong mục journal: gọn, không thụt dòng (như SerializeToElement của bản cũ). Thêm loại snapshot mới thì thêm vào đây.</summary>
[JsonSerializable(typeof(DnsRouteSnapshot))]
[JsonSerializable(typeof(AutoProxySnapshot))]
[JsonSerializable(typeof(HostsSnapshot))]
internal sealed partial class SnapshotJsonContext : JsonSerializerContext;
