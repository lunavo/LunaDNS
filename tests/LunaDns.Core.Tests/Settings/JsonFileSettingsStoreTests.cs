using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
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
    public void Old_settings_without_domains_load_with_default_selection()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "SplitMode": "FirstByte", "SplitDelayMs": 0 }""");

        var settings = new JsonFileSettingsStore(path).Load();

        Assert.Equal([DomainGroup.Steam, DomainGroup.Custom], settings.Domains.EnabledGroups);
        Assert.Empty(settings.Domains.Custom);
        Assert.False(settings.SplitRecords);
    }

    [Fact]
    public void Domains_round_trip_and_bad_entries_are_dropped_on_load()
    {
        var path = PathOf("settings.json");
        var store = new JsonFileSettingsStore(path);
        store.Save(new AppSettings { Domains = new DomainSelection { EnabledGroups = [DomainGroup.Reddit], Custom = [new("example.org"), new("com")] } });

        var loaded = store.Load();

        Assert.Equal([DomainGroup.Reddit], loaded.Domains.EnabledGroups);
        Assert.Equal([new CustomDomain("example.org")], loaded.Domains.Custom);
        Assert.Equal(1, store.DroppedDomains);
    }

    [Theory]
    [InlineData("""{ "Domains": { "Custom": null } }""", 0)]
    [InlineData("""{ "Domains": { "Custom": [null, { "Domain": "example.org" }] } }""", 1)]
    [InlineData("""{ "Domains": { "EnabledGroups": null } }""", 0)]
    [InlineData("""{ "Domains": { "EnabledGroups": [99, "Reddit"] } }""", 1)]
    public void Hand_edited_nulls_and_unknown_groups_load_without_crashing(string json, int expectedDropped)
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, json);
        var store = new JsonFileSettingsStore(path);

        var loaded = store.Load();

        Assert.NotNull(loaded.Domains.EnabledGroups);
        Assert.NotNull(loaded.Domains.Custom);
        Assert.Equal(DomainSelectionDefect.None, loaded.Domains.CheckShape());
        Assert.Equal(expectedDropped, store.DroppedDomains);
        _ = FeatureDomains.Routed(loaded.Domains);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonFileSettingsStore(PathOf("sub/settings.json"));
        var settings = new AppSettings { SplitMode = SplitMode.MidSni, SplitRecords = true, SplitDelayMs = 5, PreferGoogleDoh = true, CheckPubg = true, PubgWarningAccepted = true, DomainsIntroSeen = true };

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

    [Fact]
    public void Old_settings_with_prefer_google_load_as_google()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "PreferGoogleDoh": true }""");

        var settings = new JsonFileSettingsStore(path).Load();

        Assert.Null(settings.Doh);
        Assert.Null(settings.CustomDoh);
        Assert.Equal(DohProvider.Google, settings.EffectiveDoh());
    }

    [Fact]
    public void Doh_choice_and_custom_url_round_trip()
    {
        var store = new JsonFileSettingsStore(PathOf("settings.json"));
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q", ManualAddresses = ["1.2.3.4"], ResolvedAddresses = ["5.6.7.8"] };

        store.Save(new AppSettings().WithDoh(DohProvider.Custom) with { CustomDoh = custom });
        var loaded = store.Load();

        Assert.Equal(DohProvider.Custom, loaded.Doh);
        Assert.Equal(custom.Endpoint, loaded.CustomDoh!.Endpoint);
        Assert.Equal(custom.ManualAddresses, loaded.CustomDoh.ManualAddresses);
        Assert.Equal(custom.ResolvedAddresses, loaded.CustomDoh.ResolvedAddresses);
        Assert.Contains("\"Custom\"", File.ReadAllText(PathOf("settings.json")), StringComparison.Ordinal);
        Assert.DoesNotContain("EffectiveDoh", File.ReadAllText(PathOf("settings.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Custom_doh_with_missing_lists_loads_with_empty_lists()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "Doh": "Custom", "CustomDoh": { "Endpoint": "https://dns.example.net/q" } }""");

        var settings = new JsonFileSettingsStore(path).Load();

        Assert.Empty(settings.CustomDoh!.ManualAddresses);
        Assert.Empty(settings.CustomDoh.ResolvedAddresses);
        Assert.Equal("Cloudflare", settings.DohServers()[0].Name);
    }

    [Fact]
    public void Old_settings_without_domain_list_switch_load_as_enabled()
    {
        var path = PathOf("settings.json");
        File.WriteAllText(path, """{ "SplitMode": "FirstByte" }""");
        Assert.True(new JsonFileSettingsStore(path).Load().DomainListUpdates);
    }

    [Fact]
    public void Domain_list_switch_round_trips()
    {
        var store = new JsonFileSettingsStore(PathOf("settings.json"));
        store.Save(new AppSettings { DomainListUpdates = false });
        Assert.False(store.Load().DomainListUpdates);
    }
}
