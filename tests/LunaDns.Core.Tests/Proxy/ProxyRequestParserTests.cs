using System.Text;
using LunaDns.Core.Proxy;

namespace LunaDns.Core.Tests.Proxy;

public class ProxyRequestParserTests
{
    private static byte[] Head(string requestLine) =>
        Encoding.ASCII.GetBytes(requestLine + "\r\nHost: x\r\n\r\n");

    [Fact]
    public void Parses_CONNECT_host_and_port()
    {
        Assert.True(ProxyRequestParser.TryParse(Head("CONNECT store.steampowered.com:443 HTTP/1.1"), out var r));

        Assert.Equal("CONNECT", r.Method);
        Assert.Equal("store.steampowered.com", r.ConnectHost);
        Assert.Equal(443, r.ConnectPort);
    }

    [Fact]
    public void Normalizes_CONNECT_host()
    {
        Assert.True(ProxyRequestParser.TryParse(Head("CONNECT Store.SteamPowered.COM.:443 HTTP/1.1"), out var r));

        Assert.Equal("store.steampowered.com", r.ConnectHost);
    }

    [Fact]
    public void Parses_GET_without_connect_host()
    {
        Assert.True(ProxyRequestParser.TryParse(Head("GET /proxy.pac HTTP/1.1"), out var r));

        Assert.Equal("GET", r.Method);
        Assert.Equal("/proxy.pac", r.Target);
        Assert.Null(r.ConnectHost);
    }

    [Fact]
    public void Accepts_HTTP_1_0() =>
        Assert.True(ProxyRequestParser.TryParse(Head("CONNECT store.steampowered.com:443 HTTP/1.0"), out _));

    [Theory]
    [InlineData("CONNECT store.steampowered.com HTTP/1.1")]
    [InlineData("CONNECT store.steampowered.com:0 HTTP/1.1")]
    [InlineData("CONNECT store.steampowered.com:70000 HTTP/1.1")]
    [InlineData("CONNECT store.steampowered.com:+443 HTTP/1.1")]
    [InlineData("CONNECT [::1]:443 HTTP/1.1")]
    [InlineData("CONNECT :443 HTTP/1.1")]
    [InlineData("GET /proxy.pac")]
    [InlineData("GET  /proxy.pac HTTP/1.1")]
    [InlineData("GET /proxy.pac HTTP/2")]
    [InlineData("get /proxy.pac HTTP/1.1")]
    public void Rejects_malformed_request_line(string line) =>
        Assert.False(ProxyRequestParser.TryParse(Head(line), out _));

    [Fact]
    public void Rejects_control_and_non_ascii_bytes()
    {
        byte[] head = [.. "GET /"u8, 0x01, 0xC3, 0xA9, .. " HTTP/1.1\r\n\r\n"u8];

        Assert.False(ProxyRequestParser.TryParse(head, out _));
    }

    [Fact]
    public void Rejects_head_without_line_ending() =>
        Assert.False(ProxyRequestParser.TryParse("GET /proxy.pac HTTP/1.1"u8, out _));

    [Fact]
    public void FindHeadEnd_points_just_after_blank_line()
    {
        var data = Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n\r\nEXTRA");

        Assert.Equal(data.Length - 5, ProxyRequestParser.FindHeadEnd(data));
    }

    [Fact]
    public void FindHeadEnd_is_minus_one_when_incomplete() =>
        Assert.Equal(-1, ProxyRequestParser.FindHeadEnd("GET / HTTP/1.1\r\nHost: x\r\n"u8));
}
