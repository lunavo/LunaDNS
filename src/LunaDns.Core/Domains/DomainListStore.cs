using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Domains;

public sealed record DomainListStoreData(DomainListUpdate? Current, long HighestSerial, DateTimeOffset? CheckedAt)
{
    public static DomainListStoreData Empty { get; } = new(null, 0, null);
}

/// <summary>
/// domain-list.json trong thư mục dữ liệu đã có ACL (spec D4 "Lưu trữ và quyền"). Lúc nạp luôn xác minh lại chữ ký;
/// sai thì chạy với lớp 1 nhưng vẫn giữ HighestSerial (chống hạ cấp). Ghi: file tạm rồi move, như JsonFileSettingsStore.
/// </summary>
public sealed class DomainListStore(string path, IReadOnlyDictionary<string, string> keys)
{
    public DomainListStoreData Load()
    {
        if (!File.Exists(path)) return DomainListStoreData.Empty;
        DomainListFile? file;
        try
        {
            using var stream = File.OpenRead(path);
            file = JsonSerializer.Deserialize(stream, DomainListJsonContext.Default.DomainListFile);
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return DomainListStoreData.Empty;
        }
        if (file is null) return DomainListStoreData.Empty;

        DomainListUpdate? current = null;
        if (file.Envelope is { } envelope)
        {
            try
            {
                var verified = DomainListVerifier.Verify(Convert.FromBase64String(envelope), keys, file.HighestSerial, requireNewer: false);
                if (verified.Outcome == DomainListOutcome.Accepted) current = verified.Update;
            }
            catch (FormatException)
            {
                // Base64 hỏng: coi như không có bản lớp 2.
            }
        }
        return new DomainListStoreData(current, file.HighestSerial, file.CheckedAt);
    }

    public void Save(DomainListStoreData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var file = new DomainListFile(data.Current is { } c ? Convert.ToBase64String(c.File) : null, data.HighestSerial, data.CheckedAt);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(file, DomainListJsonContext.Default.DomainListFile));
        File.Move(temp, path, overwrite: true);
    }
}
