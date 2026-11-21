using System.Globalization;
using System.Text.Json;
using LunaDns.Core.Domains;

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
            using var stream = File.OpenRead(path);
            var loaded = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
#pragma warning disable CA1508 // JSON "Domains": null ghi đè giá trị mặc định, nên vẫn có thể null lúc chạy.
            var domains = (loaded.Domains ?? new DomainSelection()).Sanitized(out var dropped);
#pragma warning restore CA1508
            DroppedDomains = dropped;
            return loaded with { Domains = domains };
        }
        catch (JsonException)
        {
            // Cùng cách với JsonFileJournalStore: giữ file hỏng để chẩn đoán, không chặn app.
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(temp, path, overwrite: true);
    }
}
