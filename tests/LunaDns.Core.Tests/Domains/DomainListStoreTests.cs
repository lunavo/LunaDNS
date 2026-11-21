using LunaDns.Core.Domains;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-domainlist-").FullName;
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 7, 0, 0, TimeSpan.Zero);

    private string FilePath => Path.Combine(_dir, "domain-list.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static DomainListUpdate Update(long serial) =>
        DomainListVerifier.Verify(Signed(serial, new() { ["steam"] = ["cdn.example.net"] }), Keys, 0).Update!;

    [Fact]
    public void Missing_file_is_empty() =>
        Assert.Equal(DomainListStoreData.Empty, new DomainListStore(FilePath, Keys).Load());

    [Fact]
    public void Save_then_load_round_trips_and_reverifies()
    {
        var store = new DomainListStore(FilePath, Keys);
        store.Save(new DomainListStoreData(Update(7), 7, Now));

        var loaded = store.Load();

        Assert.Equal(7, loaded.Current!.Serial);
        Assert.Equal(["cdn.example.net"], loaded.Current.Groups[DomainGroup.Steam]);
        Assert.Equal(7, loaded.HighestSerial);
        Assert.Equal(Now, loaded.CheckedAt);
    }

    [Fact]
    public void Envelope_with_unknown_key_loads_without_current_but_keeps_highest_serial()
    {
        new DomainListStore(FilePath, Keys).Save(new DomainListStoreData(Update(7), 9, Now));

        var loaded = new DomainListStore(FilePath, new Dictionary<string, string>()).Load();

        Assert.Null(loaded.Current);
        Assert.Equal(9, loaded.HighestSerial);
    }

    [Fact]
    public void Corrupt_file_is_moved_aside_and_empty()
    {
        File.WriteAllText(FilePath, "{ not json");

        Assert.Equal(DomainListStoreData.Empty, new DomainListStore(FilePath, Keys).Load());
        Assert.Single(Directory.GetFiles(_dir, "domain-list.json.corrupt-*"));
    }

    [Fact]
    public void Save_without_current_keeps_serial_and_check_time()
    {
        var store = new DomainListStore(FilePath, Keys);
        store.Save(new DomainListStoreData(null, 3, Now));
        Assert.Equal(new DomainListStoreData(null, 3, Now), store.Load());
    }
}
