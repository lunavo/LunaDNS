using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Tests;

/// <summary>Test ghi snapshot bất kỳ (kể cả kiểu ẩn danh) bằng serializer reflection; code chạy thật truyền JsonTypeInfo sinh sẵn.</summary>
internal static class ChangeJournalTestExtensions
{
    public static JournalEntry Record<T>(this ChangeJournal journal, string feature, string op, T snapshot) =>
        journal.Record(feature, op, snapshot, (JsonTypeInfo<T>)JsonSerializerOptions.Default.GetTypeInfo(typeof(T)));
}
