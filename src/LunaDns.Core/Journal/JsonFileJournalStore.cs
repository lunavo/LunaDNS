using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Journal;

/// <summary>Lưu journal ra file JSON, ghi nguyên tử (tmp rồi replace). Thư mục cha phải có sẵn.</summary>
public sealed class JsonFileJournalStore(string path) : IJournalStore
{
    public IReadOnlyList<JournalEntry> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, JournalJsonContext.Default.ListJournalEntry) ?? [];
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return [];
        }
    }

    public void Save(IReadOnlyList<JournalEntry> entries)
    {
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, entries, JournalJsonContext.Default.IReadOnlyListJournalEntry);
            stream.Flush(flushToDisk: true);
        }
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }
}
