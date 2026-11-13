using LunaDns.Core.Journal;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Journal;

public class ChangeJournalTests
{
    private static readonly string[] ThreeOne = ["3", "1"];
    private static readonly string[] Two = ["2"];
    private static readonly string[] One = ["1"];

    private readonly InMemoryJournalStore _store = new();
    private readonly FakeReverter _dns = new("dns-route");
    private readonly FakeReverter _pac = new("auto-proxy");

    private ChangeJournal NewJournal() => new(_store, [_dns, _pac]);

    [Fact]
    public void Record_persists_before_returning()
    {
        var j = NewJournal();

        var e = j.Record("dns", "dns-route", new { value = "a" });

        Assert.Equal(1, _store.SaveCount);
        Assert.Equal(e.Id, Assert.Single(_store.Saved).Id);
        Assert.Equal("a", e.Snapshot.GetProperty("value").GetString());
    }

    [Fact]
    public void Record_uses_time_provider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));
        var j = new ChangeJournal(_store, [_dns], time);

        Assert.Equal(time.GetUtcNow(), j.Record("dns", "dns-route", new { value = "a" }).AppliedAt);
    }

    [Fact]
    public void RollbackAll_reverts_in_reverse_order_and_clears()
    {
        var j = NewJournal();
        j.Record("dns", "dns-route", new { value = "1" });
        j.Record("proxy", "auto-proxy", new { value = "2" });
        j.Record("dns", "dns-route", new { value = "3" });

        var r = j.RollbackAll();

        Assert.True(r.Success);
        Assert.Equal(3, r.Reverted);
        Assert.Equal(ThreeOne, _dns.Reverted);
        Assert.Equal(Two, _pac.Reverted);
        Assert.Empty(j.Entries);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Failed_entries_stay_in_journal_and_others_still_revert()
    {
        var j = NewJournal();
        j.Record("dns", "dns-route", new { value = "1" });
        j.Record("proxy", "auto-proxy", new { value = "2" });
        _pac.FailTimes = 1;

        var r = j.RollbackAll();

        Assert.False(r.Success);
        Assert.Equal(1, r.Reverted);
        Assert.Equal("auto-proxy", Assert.Single(r.Failed).Entry.Op);
        Assert.Equal("auto-proxy", Assert.Single(_store.Saved).Op);

        var again = j.RollbackAll();
        Assert.True(again.Success);
        Assert.Equal(Two, _pac.Reverted);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Unknown_op_is_a_failure_not_a_crash()
    {
        var j = NewJournal();
        j.Record("x", "unknown-op", new { value = "1" });

        var r = j.RollbackAll();

        Assert.IsType<KeyNotFoundException>(Assert.Single(r.Failed).Error);
        Assert.Single(j.Entries);
    }

    [Fact]
    public void Rollback_feature_only_touches_that_feature()
    {
        var j = NewJournal();
        j.Record("dns", "dns-route", new { value = "1" });
        j.Record("proxy", "auto-proxy", new { value = "2" });

        var r = j.Rollback("dns");

        Assert.Equal(1, r.Reverted);
        Assert.Equal(One, _dns.Reverted);
        Assert.Empty(_pac.Reverted);
        Assert.Equal("proxy", Assert.Single(j.Entries).Feature);
    }

    [Fact]
    public void Remove_deletes_entry_and_persists()
    {
        var j = NewJournal();
        var e = j.Record("dns", "dns-route", new { value = "1" });

        j.Remove(e.Id);

        Assert.Empty(j.Entries);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Loads_existing_entries_from_store_on_construction()
    {
        NewJournal().Record("dns", "dns-route", new { value = "1" });   // phiên trước "crash"

        var r = NewJournal().RollbackAll();                              // phiên mới

        Assert.Equal(1, r.Reverted);
        Assert.Equal(One, _dns.Reverted);
    }
}
