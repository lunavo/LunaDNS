using System.Diagnostics.CodeAnalysis;

namespace LunaDns.Core.Dns;

/// <summary>Cache response DoH theo (tên, type, class). TTL lấy từ bản ghi, tối đa 15 phút (spec 4.2).</summary>
public sealed class DnsCache
{
    public const uint MaxTtlSeconds = 15 * 60;

    private readonly TimeProvider _time;
    private readonly int _capacity;
    private readonly Dictionary<DnsQuestion, Entry> _entries = [];
    private readonly object _gate = new();

    public DnsCache(TimeProvider? time = null, int capacity = 2048)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _time = time ?? TimeProvider.System;
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    public void Store(DnsQuestion question, ReadOnlySpan<byte> response)
    {
        var ttl = DnsWire.GetMinTtl(response);
        if (ttl is null or 0) return;
        var now = _time.GetUtcNow();
        var entry = new Entry(response.ToArray(), now, now.AddSeconds(Math.Min(ttl.Value, MaxTtlSeconds)));
        lock (_gate)
        {
            if (_entries.Count >= _capacity && !_entries.ContainsKey(question))
                _entries.Remove(_entries.MinBy(e => e.Value.ExpiresAt).Key);
            _entries[question] = entry;
        }
    }

    public bool TryGet(DnsQuestion question, ushort id, [NotNullWhen(true)] out byte[]? response)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_entries.TryGetValue(question, out var entry))
            {
                if (now < entry.ExpiresAt)
                {
                    var elapsed = (uint)(now - entry.StoredAt).TotalSeconds;
                    response = DnsWire.RewriteForClient(entry.Response, id, elapsed, MaxTtlSeconds);
                    return true;
                }
                _entries.Remove(question);
            }
        }
        response = null;
        return false;
    }

    private sealed record Entry(byte[] Response, DateTimeOffset StoredAt, DateTimeOffset ExpiresAt);
}
