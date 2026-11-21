using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LunaDns.Core.Journal;

/// <summary>
/// Nhật ký thay đổi hệ thống (spec 4.5). Gọi Record TRƯỚC khi đổi hệ thống;
/// hoàn tác chạy LIFO, mục lỗi được giữ lại để lần sau thử tiếp.
/// </summary>
public sealed class ChangeJournal
{
    private readonly IJournalStore _store;
    private readonly Dictionary<string, IChangeReverter> _reverters;
    private readonly TimeProvider _time;
    private readonly List<JournalEntry> _entries;
    private readonly object _gate = new();

    public ChangeJournal(IJournalStore store, IEnumerable<IChangeReverter> reverters, TimeProvider? time = null)
    {
        _store = store;
        _reverters = reverters.ToDictionary(r => r.Op, StringComparer.Ordinal);
        _time = time ?? TimeProvider.System;
        _entries = [.. store.Load()];
    }

    public IReadOnlyList<JournalEntry> Entries
    {
        get { lock (_gate) return [.. _entries]; }
    }

    /// <param name="typeInfo">Metadata sinh sẵn của snapshot (JSON reflection bị tắt trong bản publish trimmed).</param>
    public JournalEntry Record<T>(string feature, string op, T snapshot, JsonTypeInfo<T> typeInfo)
    {
        var entry = new JournalEntry(Guid.NewGuid(), feature, op, JsonSerializer.SerializeToElement(snapshot, typeInfo), _time.GetUtcNow());
        lock (_gate)
        {
            _entries.Add(entry);
            _store.Save(_entries);
        }
        return entry;
    }

    public void Remove(Guid id)
    {
        lock (_gate)
        {
            if (_entries.RemoveAll(e => e.Id == id) > 0) _store.Save(_entries);
        }
    }

    public RollbackResult RollbackAll() => RollbackWhere(_ => true);

    public RollbackResult Rollback(string feature) =>
        RollbackWhere(e => string.Equals(e.Feature, feature, StringComparison.Ordinal));

    private RollbackResult RollbackWhere(Func<JournalEntry, bool> predicate)
    {
        lock (_gate)
        {
            var reverted = 0;
            var failed = new List<RollbackFailure>();
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                var entry = _entries[i];
                if (!predicate(entry)) continue;
                try
                {
                    if (!_reverters.TryGetValue(entry.Op, out var reverter))
                        throw new KeyNotFoundException($"Không có reverter cho thao tác '{entry.Op}'.");
                    reverter.Revert(entry.Snapshot);
                    _entries.RemoveAt(i);
                    _store.Save(_entries);
                    reverted++;
                }
                catch (Exception ex)
                {
                    failed.Add(new RollbackFailure(entry, ex));
                }
            }
            return new RollbackResult(reverted, failed);
        }
    }
}
