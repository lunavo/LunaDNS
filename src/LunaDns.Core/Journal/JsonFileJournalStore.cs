using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Journal;

/// <summary>
/// Lưu journal ra file JSON, ghi nguyên tử (tmp rồi replace). Thư mục cha phải có sẵn.
/// Load không bao giờ ném (B5): file hỏng hay không đọc được thì cách ly sang <c>.corrupt-&lt;giờ&gt;</c> nếu được và trả rỗng;
/// thay đổi còn sót do file đó giữ được <see cref="LunaDns.Core.Platform.OrphanSweeper"/> dọn theo dấu LunaDNS.
/// </summary>
public sealed partial class JsonFileJournalStore(string path, ILogger? logger = null) : IJournalStore
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    public IReadOnlyList<JournalEntry> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            List<JournalEntry>? entries;
            using (var stream = File.OpenRead(path))
                entries = JsonSerializer.Deserialize(stream, JournalJsonContext.Default.ListJournalEntry);
            // Bộ sinh sẵn không kiểm tra non-nullable: "null", "[null]" hay mục thiếu Op đọc lên được nhưng dùng thì vỡ.
            if (entries is not null && entries.All(e => e is { Feature: not null, Op: not null })) return entries;
            LogUnreadable(_logger, path, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LogUnreadable(_logger, path, ex);
        }
        Quarantine();
        return [];
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

    /// <summary>Đổi tên để giữ lại cho chẩn đoán. Lỗi thì để nguyên: lần Save sau ghi đè.</summary>
    private void Quarantine()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.corrupt-{stamp}";
        for (var n = 1; File.Exists(target) || Directory.Exists(target); n++)
            target = string.Create(CultureInfo.InvariantCulture, $"{path}.corrupt-{stamp}-{n}");
        try
        {
            File.Move(path, target);
            LogQuarantined(_logger, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogQuarantineFailed(_logger, path, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Không đọc được journal {Path}; coi như rỗng, các thay đổi còn sót sẽ được quét theo dấu LunaDNS.")]
    private static partial void LogUnreadable(ILogger logger, string path, Exception? error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Đã cách ly journal hỏng sang {Target}.")]
    private static partial void LogQuarantined(ILogger logger, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không cách ly được journal {Path}; để nguyên.")]
    private static partial void LogQuarantineFailed(ILogger logger, string path, Exception error);
}
