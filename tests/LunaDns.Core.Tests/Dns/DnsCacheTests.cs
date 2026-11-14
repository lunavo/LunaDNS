using System.Buffers.Binary;
using LunaDns.Core.Dns;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Dns;

public class DnsCacheTests
{
    private const string Name = "store.steampowered.com";
    private static readonly DnsQuestion StoreA = new(Name, TestDnsMessage.TypeA, 1);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero));

    private static uint FirstTtl(byte[] msg)
    {
        var offsets = new List<int>();
        Assert.True(DnsWire.TryGetTtlOffsets(msg, offsets));
        return BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(offsets[0]));
    }

    [Fact]
    public void Hit_returns_response_with_client_id()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));

        Assert.True(cache.TryGet(StoreA, 0x4321, out var r));
        Assert.Equal(0x4321, DnsWire.ReadId(r));
        Assert.Equal(300u, FirstTtl(r));
    }

    [Fact]
    public void Ttl_counts_down_with_time()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));
        _time.Advance(TimeSpan.FromSeconds(100));

        Assert.True(cache.TryGet(StoreA, 1, out var r));
        Assert.Equal(200u, FirstTtl(r));
    }

    [Fact]
    public void Expired_entry_is_removed()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));
        _time.Advance(TimeSpan.FromSeconds(300));

        Assert.False(cache.TryGet(StoreA, 1, out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Ttl_is_capped_at_15_minutes()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 3600));

        Assert.True(cache.TryGet(StoreA, 1, out var r));
        Assert.Equal(DnsCache.MaxTtlSeconds, FirstTtl(r));
        _time.Advance(TimeSpan.FromSeconds(DnsCache.MaxTtlSeconds));
        Assert.False(cache.TryGet(StoreA, 1, out _));
    }

    [Fact]
    public void Zero_ttl_is_not_cached()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 0));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Response_without_records_is_not_cached()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Key_includes_record_type()
    {
        var cache = new DnsCache(_time);
        cache.Store(StoreA, TestDnsMessage.Response(Name, 0, 300));
        Assert.False(cache.TryGet(StoreA with { Type = TestDnsMessage.TypeAaaa }, 1, out _));
    }

    [Fact]
    public void Full_cache_evicts_entry_expiring_first()
    {
        var cache = new DnsCache(_time, capacity: 2);
        var a = new DnsQuestion("a.steampowered.com", TestDnsMessage.TypeA, 1);
        var b = new DnsQuestion("b.steampowered.com", TestDnsMessage.TypeA, 1);
        var c = new DnsQuestion("c.steampowered.com", TestDnsMessage.TypeA, 1);

        cache.Store(a, TestDnsMessage.Response(a.Name, 0, 600));
        cache.Store(b, TestDnsMessage.Response(b.Name, 0, 60));
        cache.Store(c, TestDnsMessage.Response(c.Name, 0, 300));

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet(b, 1, out _));
        Assert.True(cache.TryGet(a, 1, out _));
        Assert.True(cache.TryGet(c, 1, out _));
    }

    [Fact]
    public void Stored_bytes_are_copied()
    {
        var cache = new DnsCache(_time);
        var response = TestDnsMessage.Response(Name, 0, 300);
        cache.Store(StoreA, response);
        Array.Clear(response);

        Assert.True(cache.TryGet(StoreA, 1, out var r));
        Assert.Equal(300u, FirstTtl(r));
    }
}
