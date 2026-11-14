using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public class DnsWireTests
{
    [Fact]
    public void Parses_single_question_and_normalizes_name()
    {
        Assert.True(DnsWire.TryParseQuery(TestDnsMessage.Query("Store.SteamPowered.com", TestDnsMessage.TypeAaaa), out var q));
        Assert.Equal(0x1234, q.Id);
        Assert.Equal(0, q.Opcode);
        Assert.Equal(new DnsQuestion("store.steampowered.com", TestDnsMessage.TypeAaaa, 1), q.Question);
        Assert.Equal(DnsWire.MinUdpPayload, q.MaxUdpPayload);
    }

    [Theory]
    [InlineData(1232, 1232)]
    [InlineData(100, 512)]
    [InlineData(65000, 4096)]
    public void Reads_edns_udp_size_clamped(int advertised, int expected)
    {
        Assert.True(DnsWire.TryParseQuery(TestDnsMessage.Query("steampowered.com", ednsUdpSize: advertised), out var q));
        Assert.Equal(expected, q.MaxUdpPayload);
    }

    [Fact]
    public void Reads_opcode()
    {
        Assert.True(DnsWire.TryParseQuery(TestDnsMessage.Query("steampowered.com", flags: 0x0100 | (2 << 11)), out var q));
        Assert.Equal(2, q.Opcode);
    }

    public static TheoryData<string, byte[]> Malformed => new()
    {
        { "ngắn hơn header", new byte[11] },
        { "là response (QR=1)", TestDnsMessage.Response("steampowered.com", 1, 300) },
        { "hai question", WithByte(TestDnsMessage.Query("steampowered.com"), 5, 2) },
        { "tên bị cắt cụt", TestDnsMessage.Query("steampowered.com")[..20] },
        { "label chứa dấu chấm", WithByte(TestDnsMessage.Query("ab.steampowered.com"), 14, (byte)'.') },
        { "con trỏ nén trong question", [.. TestDnsMessage.Query("x.com")[..12], 0xC0, 0x0C, 0, 1, 0, 1] },
    };

    [Theory]
    [MemberData(nameof(Malformed))]
    public void Rejects_malformed_query(string because, byte[] packet) =>
        Assert.False(DnsWire.TryParseQuery(packet, out _), because);

    [Fact]
    public void Refused_response_keeps_id_rd_and_question_and_drops_opt()
    {
        var plain = TestDnsMessage.Query("google.com");
        var withOpt = TestDnsMessage.Query("google.com", ednsUdpSize: 1232);

        var r = DnsWire.BuildHeaderOnlyResponse(withOpt, DnsRcode.Refused);

        Assert.Equal(plain.Length, r.Length);
        Assert.Equal(0x1234, DnsWire.ReadId(r));
        Assert.Equal(0x8185, TestDnsMessage.U16(r, 2)); // QR, RD, RA, REFUSED
        Assert.Equal(DnsRcode.Refused, DnsWire.ReadRcode(r));
        Assert.Equal(1, TestDnsMessage.U16(r, 4));
        Assert.Equal(0, TestDnsMessage.U16(r, 6) + TestDnsMessage.U16(r, 8) + TestDnsMessage.U16(r, 10));
        Assert.Equal(plain[12..], r[12..]);
    }

    [Fact]
    public void Truncated_response_sets_tc_bit()
    {
        var r = DnsWire.BuildHeaderOnlyResponse(TestDnsMessage.Query("store.steampowered.com"), DnsRcode.NoError, truncated: true);
        Assert.Equal(0x8380, TestDnsMessage.U16(r, 2)); // QR, TC, RD, RA, NOERROR
    }

    [Fact]
    public void Header_without_readable_question_gives_header_only()
    {
        var junk = new byte[12];
        junk[5] = 1; // QDCOUNT = 1 nhưng không có question

        var r = DnsWire.BuildHeaderOnlyResponse(junk, DnsRcode.FormErr);

        Assert.Equal(12, r.Length);
        Assert.Equal(0, TestDnsMessage.U16(r, 4));
        Assert.Equal(DnsRcode.FormErr, DnsWire.ReadRcode(r));
    }

    private static byte[] WithByte(byte[] message, int at, byte value)
    {
        var copy = (byte[])message.Clone();
        copy[at] = value;
        return copy;
    }
}
