using System.Globalization;
using System.Text.Json;

namespace LunaDns.Core.Settings;

/// <summary>profiles.json: ghi file tạm rồi thay; file hỏng thì cất sang bên cạnh và chạy với danh sách rỗng (như JsonFileSettingsStore).</summary>
public sealed class NetworkProfileStore
{
    public const int MaxProfiles = 32;

    private readonly string _path;
    private readonly object _gate = new();
    private List<NetworkProfile> _profiles;

    public NetworkProfileStore(string path)
    {
        _path = path;
        _profiles = Load(path);
    }

    public IReadOnlyList<NetworkProfile> All
    {
        get { lock (_gate) return [.. _profiles]; }
    }

    public NetworkProfile? Find(string id)
    {
        lock (_gate) return _profiles.Find(p => p.Id == id);
    }

    public void Upsert(NetworkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Change(list =>
        {
            list.RemoveAll(p => p.Id == profile.Id);
            list.Add(profile);
            while (list.Count > MaxProfiles) list.Remove(list.MinBy(p => p.LastSeen)!);
        });
    }

    public void Touch(string id, DateTimeOffset at) => Replace(id, p => p with { LastSeen = at });

    public void MarkStale(string id) => Replace(id, p => p with { Stale = true });

    public void Remove(string id) => Change(list => list.RemoveAll(p => p.Id == id));

    public void Clear() => Change(list => list.Clear());

    private void Replace(string id, Func<NetworkProfile, NetworkProfile> change) => Change(list =>
    {
        var i = list.FindIndex(p => p.Id == id);
        if (i >= 0) list[i] = change(list[i]);
    });

    private void Change(Action<List<NetworkProfile>> change)
    {
        lock (_gate)
        {
            var next = new List<NetworkProfile>(_profiles);
            change(next);
            Save(next);
            _profiles = next;
        }
    }

    private void Save(List<NetworkProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(profiles, SettingsJsonContext.Default.ListNetworkProfile));
        File.Move(temp, _path, overwrite: true);
    }

    private static List<NetworkProfile> Load(string path)
    {
        if (!File.Exists(path)) return [];
        try
        {
            var list = JsonSerializer.Deserialize(File.ReadAllBytes(path), SettingsJsonContext.Default.ListNetworkProfile) ?? [];
            list.RemoveAll(p => p is null || string.IsNullOrEmpty(p.Id) || p.Candidate is null);
            return list;
        }
        catch (JsonException)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            File.Move(path, $"{path}.corrupt-{stamp}");
            return [];
        }
    }
}
