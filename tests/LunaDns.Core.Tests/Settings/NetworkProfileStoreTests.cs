using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Settings;

public sealed class NetworkProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lunadns-profiles-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "profiles.json");
    private static readonly DateTimeOffset T0 = new(2026, 11, 28, 8, 0, 0, TimeSpan.Zero);

    public NetworkProfileStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static NetworkProfile Profile(string id, DateTimeOffset seen) =>
        new(id, false, "Wi-Fi · 192.168.1.1", DohProvider.Google, new TuneCandidate(new HelloSplit(SplitMode.MidSni, Records: true), 10), T0, seen);

    [Fact]
    public void RoundTrip()
    {
        new NetworkProfileStore(FilePath).Upsert(Profile("a", T0));
        var loaded = new NetworkProfileStore(FilePath).Find("a");
        Assert.Equal(Profile("a", T0), loaded);
    }

    [Fact]
    public void MissingFile_Empty() => Assert.Empty(new NetworkProfileStore(FilePath).All);

    [Fact]
    public void CorruptFile_MovedAside_Empty()
    {
        File.WriteAllText(FilePath, "{ not json");
        var store = new NetworkProfileStore(FilePath);
        Assert.Empty(store.All);
        Assert.Single(Directory.GetFiles(_dir, "profiles.json.corrupt-*"));
    }

    [Fact]
    public void Upsert_SameId_Overwrites()
    {
        var store = new NetworkProfileStore(FilePath);
        store.Upsert(Profile("a", T0));
        store.Upsert(Profile("a", T0) with { Doh = DohProvider.Quad9 });
        Assert.Equal(DohProvider.Quad9, Assert.Single(store.All).Doh);
    }

    [Fact]
    public void Full_DropsOldestLastSeen()
    {
        var store = new NetworkProfileStore(FilePath);
        for (var i = 0; i < NetworkProfileStore.MaxProfiles; i++) store.Upsert(Profile($"p{i}", T0.AddMinutes(i)));
        store.Upsert(Profile("new", T0.AddDays(1)));
        Assert.Equal(NetworkProfileStore.MaxProfiles, store.All.Count);
        Assert.Null(store.Find("p0"));
        Assert.NotNull(store.Find("new"));
    }

    [Fact]
    public void Touch_MarkStale_Remove_Clear()
    {
        var store = new NetworkProfileStore(FilePath);
        store.Upsert(Profile("a", T0));
        store.Upsert(Profile("b", T0));
        store.Touch("a", T0.AddHours(1));
        store.MarkStale("a");
        var a = new NetworkProfileStore(FilePath).Find("a")!;
        Assert.Equal(T0.AddHours(1), a.LastSeen);
        Assert.True(a.Stale);
        store.Remove("a");
        Assert.Null(store.Find("a"));
        store.Clear();
        Assert.Empty(new NetworkProfileStore(FilePath).All);
    }
}
