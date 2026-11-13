using System.Text;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Tls;

public class ClientHelloParserTests
{
    [Fact]
    public void Finds_sni_host_and_offset()
    {
        var hello = TestClientHello.Build("store.steampowered.com");

        Assert.True(ClientHelloParser.TryFindSni(hello, out var sni));
        Assert.Equal("store.steampowered.com", sni.Host);
        Assert.Equal(sni.Host.Length, sni.Length);
        Assert.Equal(sni.Host, Encoding.ASCII.GetString(hello, sni.Offset, sni.Length));
    }

    [Fact]
    public void Finds_sni_in_hello_longer_than_one_tcp_segment()
    {
        var hello = TestClientHello.Build("steamcommunity.com", paddingLength: 2000);

        Assert.True(hello.Length > 1460);
        Assert.True(ClientHelloParser.TryFindSni(hello, out var sni));
        Assert.Equal("steamcommunity.com", sni.Host);
    }

    [Fact]
    public void Returns_false_without_sni() =>
        Assert.False(ClientHelloParser.TryFindSni(TestClientHello.Build(null, paddingLength: 16), out _));

    [Fact]
    public void Returns_false_for_non_handshake() =>
        Assert.False(ClientHelloParser.TryFindSni("GET / HTTP/1.1\r\n\r\n"u8, out _));

    [Fact]
    public void Returns_false_for_truncated_buffer()
    {
        var hello = TestClientHello.Build("store.steampowered.com");
        for (var len = 0; len < hello.Length; len++)
            Assert.False(ClientHelloParser.TryFindSni(hello.AsSpan(0, len), out _), $"len={len}");
    }

    [Fact]
    public void Record_length_reads_header()
    {
        var hello = TestClientHello.Build("a.com");
        Assert.Equal(hello.Length, ClientHelloParser.RecordLength(hello));
        Assert.Equal(hello.Length, ClientHelloParser.RecordLength(hello.AsSpan(0, 5)));
    }

    [Fact]
    public void Record_length_is_minus_one_for_non_tls()
    {
        Assert.Equal(-1, ClientHelloParser.RecordLength("GET /"u8));
        Assert.Equal(-1, ClientHelloParser.RecordLength([0x16, 0x03]));
    }
}
