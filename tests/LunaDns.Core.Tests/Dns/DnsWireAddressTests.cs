using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DnsWireAddressTests
{
    private const string Name = "store.steampowered.com";

    [Fact]
    public void BuildQuery_is_accepted_by_TryParseQuery()
    {
        var q = DnsWire.BuildQuery(Name, DnsWire.TypeA);

        Assert.True(DnsWire.TryParseQuery(q, out var parsed));
        Assert.Equal(0, parsed.Id);
        Assert.Equal(new DnsQuestion(Name, DnsWire.TypeA, 1), parsed.Question);
        Assert.Equal(0x01, q[2] & 0x01); // RD = 1
    }

    [Fact]
    public void BuildQuery_normalizes_case_and_trailing_dot() =>
        Assert.Equal(DnsWire.BuildQuery(Name, DnsWire.TypeAaaa), DnsWire.BuildQuery("Store.SteamPowered.com.", DnsWire.TypeAaaa));

    [Theory]
    [InlineData("")]
    [InlineData("a..com")]
    [InlineData("has space.com")]
    public void BuildQuery_rejects_bad_names(string bad) =>
        Assert.Throws<ArgumentException>(() => DnsWire.BuildQuery(bad, DnsWire.TypeA));

    [Fact]
    public void BuildQuery_rejects_label_longer_than_63() =>
        Assert.Throws<ArgumentException>(() => DnsWire.BuildQuery(new string('a', 64) + ".com", DnsWire.TypeA));

    [Fact]
    public void ReadAddresses_returns_every_A_record()
    {
        var addresses = DnsWire.ReadAddresses(TestDnsMessage.Response(Name, 0, 300, 300));

        Assert.Equal(2, addresses.Count);
        Assert.All(addresses, a => Assert.Equal(IPAddress.Parse("93.184.216.34"), a));
    }

    [Fact]
    public void ReadAddresses_keeps_order_and_reads_AAAA()
    {
        IPAddress[] expected = [IPAddress.Parse("23.1.2.3"), IPAddress.Parse("2a02:26f0::1"), IPAddress.Parse("23.1.2.4")];

        Assert.Equal(expected, DnsWire.ReadAddresses(TestDnsMessage.ResponseWithAddresses(Name, 0, expected)));
    }

    [Fact]
    public void ReadAddresses_ignores_OPT_in_additional() =>
        Assert.Single(DnsWire.ReadAddresses(TestDnsMessage.ResponseWithOpt(Name, 0, 300)));

    [Fact]
    public void ReadAddresses_is_empty_for_NXDOMAIN() =>
        Assert.Empty(DnsWire.ReadAddresses(TestDnsMessage.NxDomain(Name, 0, 60)));

    [Fact]
    public void ReadAddresses_is_empty_for_truncated_message()
    {
        var r = TestDnsMessage.Response(Name, 0, 300);

        Assert.Empty(DnsWire.ReadAddresses(r.AsSpan(0, r.Length - 3)));
    }

    [Fact]
    public void ReadAddresses_is_empty_for_too_short_message() =>
        Assert.Empty(DnsWire.ReadAddresses(new byte[5]));
}
