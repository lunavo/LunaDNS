using System.Text.Json;
using LunaDns.Core.Journal;

namespace LunaDns.Core.Tests.Journal;

public sealed class JsonFileJournalStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-test-").FullName;
    private string PathOf(string name) => Path.Combine(_dir, name);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static JournalEntry Entry(string value) =>
        new(Guid.NewGuid(), "dns", "dns-route", JsonSerializer.SerializeToElement(new { value }),
            new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Load_missing_file_returns_empty() =>
        Assert.Empty(new JsonFileJournalStore(PathOf("journal.json")).Load());

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = new JsonFileJournalStore(PathOf("journal.json"));
        var a = Entry("a");
        var b = Entry("b");

        store.Save([a, b]);
        var loaded = new JsonFileJournalStore(PathOf("journal.json")).Load();

        Assert.Equal(new[] { a.Id, b.Id }, loaded.Select(e => e.Id));
        Assert.Equal("b", loaded[1].Snapshot.GetProperty("value").GetString());
        Assert.Equal(a.AppliedAt, loaded[0].AppliedAt);
    }

    [Fact]
    public void Save_overwrites_and_leaves_no_tmp_file()
    {
        var store = new JsonFileJournalStore(PathOf("journal.json"));
        store.Save([Entry("a"), Entry("b")]);
        store.Save([Entry("c")]);

        Assert.Single(store.Load());
        Assert.False(File.Exists(PathOf("journal.json.tmp")));
    }

    [Fact]
    public void Corrupt_file_is_quarantined_and_load_returns_empty()
    {
        File.WriteAllText(PathOf("journal.json"), "{ không phải json");

        var loaded = new JsonFileJournalStore(PathOf("journal.json")).Load();

        Assert.Empty(loaded);
        Assert.False(File.Exists(PathOf("journal.json")));
        Assert.Single(Directory.GetFiles(_dir, "journal.json.corrupt-*"));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"Id\":\"6b1f0c8e-0000-0000-0000-000000000001\",\"Feature\":\"dns\"}]")]
    public void Structurally_bad_file_is_quarantined_and_load_returns_empty(string content)
    {
        File.WriteAllText(PathOf("journal.json"), content);

        var loaded = new JsonFileJournalStore(PathOf("journal.json")).Load();

        Assert.Empty(loaded);
        Assert.False(File.Exists(PathOf("journal.json")));
        Assert.Single(Directory.GetFiles(_dir, "journal.json.corrupt-*"));
    }

    [Fact]
    public void Second_quarantine_in_the_same_second_gets_a_new_name()
    {
        var store = new JsonFileJournalStore(PathOf("journal.json"));
        File.WriteAllText(PathOf("journal.json"), "rác 1");
        Assert.Empty(store.Load());
        File.WriteAllText(PathOf("journal.json"), "rác 2");
        Assert.Empty(store.Load());

        Assert.Equal(2, Directory.GetFiles(_dir, "journal.json.corrupt-*").Length);
    }

    [Fact]
    public void Io_error_while_reading_does_not_throw_and_load_returns_empty()
    {
        File.WriteAllText(PathOf("journal.json"), "[]");
        // Giữ file độc quyền: trên Windows mở lại sẽ lỗi chia sẻ; trên Unix FileShare.None là khoá tư vấn mà .NET cũng tôn trọng.
        using var held = new FileStream(PathOf("journal.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var loaded = new JsonFileJournalStore(PathOf("journal.json")).Load();

        Assert.Empty(loaded);
    }

    [Fact]
    public void Save_empty_list_writes_empty_array()
    {
        var store = new JsonFileJournalStore(PathOf("journal.json"));
        store.Save([]);
        Assert.Equal("[]", File.ReadAllText(PathOf("journal.json")).Trim());
    }
}
