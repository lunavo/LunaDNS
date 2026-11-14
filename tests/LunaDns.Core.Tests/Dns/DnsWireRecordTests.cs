using System.Buffers.Binary;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DnsWireRecordTests
{
    private const string Name = "store.steampowered.com";
    private static readonly DnsQuestion StoreA = new(Name, TestDnsMessage.TypeA, 1);

    [Fact]
    public void Min_ttl_is_smallest_record_ttl() =>
        Assert.Equal(60u, DnsWire.GetMinTtl(TestDnsMessage.Response(Name, 0, 300, 60, 900)));

    [Fact]
    public void Opt_record_is_ignored_for_ttl() =>
        Assert.Equal(300u, DnsWire.GetMinTtl(TestDnsMessage.ResponseWithOpt(Name, 0, 300)));

    [Fact]
    public void Nxdomain_uses_soa_ttl() =>
        Assert.Equal(120u, DnsWire.GetMinTtl(TestDnsMessage.NxDomain(Name, 0, 120)));

    [Fact]
    public void Response_without_records_has_no_ttl() =>
        Assert.Null(DnsWire.GetMinTtl(TestDnsMessage.Response(Name, 0)));

    [Fact]
    public void Malformed_response_has_no_ttl()
    {
        var full = TestDnsMessage.Response(Name, 0, 300);
        Assert.Null(DnsWire.GetMinTtl(full.AsSpan(0, full.Length - 3)));
    }

    [Fact]
    public void Rewrite_sets_client_id_and_subtracts_elapsed_without_touching_original()
    {
        var original = TestDnsMessage.Response(Name, 0, 300, 60);

        var rewritten = DnsWire.RewriteForClient(original, 0xBEEF, elapsedSeconds: 100);

        Assert.Equal(0xBEEF, DnsWire.ReadId(rewritten));
        var ttls = Ttls(rewritten);
        Assert.Equal(200u, ttls[0]);
        Assert.Equal(0u, ttls[1]);
        Assert.Equal(300u, Ttls(original)[0]);
        Assert.Equal(0, DnsWire.ReadId(original));
    }

    [Fact]
    public void Rewrite_clamps_to_max_before_subtracting() =>
        Assert.Equal(800u, Ttls(DnsWire.RewriteForClient(TestDnsMessage.Response(Name, 0, 3600), 1, 100, maxTtlSeconds: 900))[0]);

    [Fact]
    public void Rewrite_leaves_opt_record_untouched()
    {
        var original = TestDnsMessage.ResponseWithOpt(Name, 0, 300);
        var rewritten = DnsWire.RewriteForClient(original, 1, 100);
        Assert.Equal(original[^11..], rewritten[^11..]);
    }

    [Fact]
    public void Matching_response_is_recognized() =>
        Assert.True(DnsWire.IsResponseTo(TestDnsMessage.Response(Name, 0, 300), StoreA));

    [Fact]
    public void Response_name_match_ignores_case() =>
        Assert.True(DnsWire.IsResponseTo(TestDnsMessage.Response("STORE.SteamPowered.com", 0, 300), StoreA));

    [Fact]
    public void Response_for_other_name_does_not_match() =>
        Assert.False(DnsWire.IsResponseTo(TestDnsMessage.Response("other.com", 0, 300), StoreA));

    [Fact]
    public void Query_is_not_a_response() =>
        Assert.False(DnsWire.IsResponseTo(TestDnsMessage.Query(Name), StoreA));

    [Fact]
    public void Response_for_other_type_does_not_match() =>
        Assert.False(DnsWire.IsResponseTo(TestDnsMessage.Response(Name, 0, 300), StoreA with { Type = TestDnsMessage.TypeAaaa }));

    private static uint[] Ttls(byte[] msg)
    {
        var offsets = new List<int>();
        Assert.True(DnsWire.TryGetTtlOffsets(msg, offsets));
        return offsets.Select(at => BinaryPrimitives.ReadUInt32BigEndian(msg.AsSpan(at))).ToArray();
    }
}
