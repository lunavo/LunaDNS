using System.Globalization;
using System.Text.Json;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;

namespace LunaDns.Core.Settings;

/// <summary>Lưu <see cref="AppSettings"/> ra file: ghi file tạm rồi thay. File hỏng thì cất sang bên cạnh và dùng mặc định.</summary>
public sealed class JsonFileSettingsStore(string path)
{
    /// <summary>Số dòng tên miền Tuỳ chỉnh bị bỏ ở lần <see cref="Load"/> gần nhất.</summary>
    public int DroppedDomains { get; private set; }

    public AppSettings Load()
    {
        DroppedDomains = 0;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var bytes = WithoutBom(File.ReadAllBytes(path));
            var loaded = JsonSerializer.Deserialize(bytes.Span, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
#pragma warning disable CA1508 // JSON "Domains": null ghi đè giá trị mặc định, nên vẫn có thể null lúc chạy.
            var domains = (loaded.Domains ?? new DomainSelection()).Sanitized(out var dropped);
#pragma warning restore CA1508
            DroppedDomains = dropped;
            // Source generator gán null cho thuộc tính init vắng trong JSON, nên bù danh sách rỗng cho URL tự nhập.
            var custom = loaded.CustomDoh is { } c
                ? c with { ManualAddresses = AsList(c.ManualAddresses), ResolvedAddresses = AsList(c.ResolvedAddresses) }
                : null;
            // Cùng lý do: file settings cũ không có công tắc cập nhật danh sách thì coi như đang bật (D4).
            var listUpdates = HasProperty(bytes, nameof(AppSettings.DomainListUpdates)) ? loaded.DomainListUpdates : true;
            // F1: file cũ không có công tắc Tự bật lại thì coi như bật; "Wanted": null (sửa tay) thì coi như chưa muốn gì.
            var autoResume = HasProperty(bytes, nameof(AppSettings.AutoResume)) ? loaded.AutoResume : true;
            var perNetwork = HasProperty(bytes, nameof(AppSettings.RememberPerNetwork)) ? loaded.RememberPerNetwork : true;
#pragma warning disable CA1508 // như Domains: JSON null ghi đè giá trị mặc định.
            var wanted = loaded.Wanted ?? WantedFeatures.None;
#pragma warning restore CA1508
            return loaded with { Domains = domains, CustomDoh = custom, DomainListUpdates = listUpdates, AutoResume = autoResume, RememberPerNetwork = perNetwork, Wanted = wanted };
        }
        catch (JsonException)
        {
            // Cùng cách với JsonFileJournalStore: giữ file hỏng để chẩn đoán, không chặn app.
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return new AppSettings();
        }
    }

    // Overload span/byte không bỏ BOM như overload stream; file sửa bằng Notepad cũ hay PowerShell 5 có BOM.
    private static ReadOnlyMemory<byte> WithoutBom(byte[] bytes) =>
        bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes.AsMemory(3) : bytes;

    private static bool HasProperty(ReadOnlyMemory<byte> json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out _);
    }

    private static IReadOnlyList<string> AsList(IReadOnlyList<string>? list) => list ?? [];

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(temp, path, overwrite: true);
    }
}
