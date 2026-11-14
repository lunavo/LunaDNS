using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Settings;

public sealed class JsonFileSettingsStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-settings-").FullName;

    private string PathOf(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults() =>
        Assert.Equal(new AppSettings(), new JsonFileSettingsStore(PathOf("settings.json")).Load());

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonFileSettingsStore(PathOf("sub/settings.json"));
        var settings = new AppSettings { SplitMode = SplitMode.MidSni, SplitDelayMs = 5, PreferGoogleDoh = true, CheckPubg = true, PubgWarningAccepted = true };

        store.Save(settings);

        Assert.Equal(settings, store.Load());
        Assert.False(File.Exists(PathOf("sub/settings.json.tmp")));
    }

    [Fact]
    public void Corrupt_file_is_quarantined_and_defaults_used()
    {
        File.WriteAllText(PathOf("settings.json"), "{ không phải json");

        var loaded = new JsonFileSettingsStore(PathOf("settings.json")).Load();

        Assert.Equal(new AppSettings(), loaded);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
        Assert.False(File.Exists(PathOf("settings.json")));
    }

    [Fact]
    public void Enums_are_written_as_names()
    {
        new JsonFileSettingsStore(PathOf("settings.json")).Save(new AppSettings { SplitMode = SplitMode.MidSni });

        Assert.Contains("\"MidSni\"", File.ReadAllText(PathOf("settings.json")), StringComparison.Ordinal);
    }
}
