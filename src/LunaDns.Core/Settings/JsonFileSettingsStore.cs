using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Settings;

/// <summary>Lưu <see cref="AppSettings"/> ra file: ghi file tạm rồi thay. File hỏng thì cất sang bên cạnh và dùng mặc định.</summary>
public sealed class JsonFileSettingsStore(string path)
{
    public AppSettings Load()
    {
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
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
