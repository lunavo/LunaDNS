using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

public sealed class CustomDohParserTests
{
    [Theory]
    [InlineData("https://dns.example.net/dns-query")]
    [InlineData("  https://dns.example.net:8443/q  ")]
    [InlineData("https://9.9.9.9/dns-query")]
    [InlineData("https://[2620:fe::fe]/dns-query")]
    public void Accepts_https_urls(string text) =>
        Assert.True(CustomDohParser.TryParseEndpoint(text, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dns.example.net/dns-query")]
    [InlineData("http://dns.example.net/dns-query")]
    [InlineData("https://user:pass@dns.example.net/dns-query")]
    [InlineData("https://dns.example.net/dns-query#x")]
    [InlineData("ftp://dns.example.net/")]
    public void Rejects_bad_urls(string? text) =>
        Assert.False(CustomDohParser.TryParseEndpoint(text, out _));

    [Fact]
    public void Addresses_split_on_commas_and_whitespace()
    {
        Assert.True(CustomDohParser.TryParseAddresses("1.2.3.4, 5.6.7.8\n2620:fe::fe", out var addresses));
        Assert.Equal(["1.2.3.4", "5.6.7.8", "2620:fe::fe"], addresses.Select(a => a.ToString()));
    }

    [Fact]
    public void Empty_address_box_is_valid_and_empty()
    {
        Assert.True(CustomDohParser.TryParseAddresses("  ", out var addresses));
        Assert.Empty(addresses);
    }

    [Theory]
    [InlineData("1.2.3.4, abc")]
    [InlineData("1")]          // IPAddress.TryParse nhận "1" thành 0.0.0.1: phải từ chối
    [InlineData("300.1.1.1")]
    public void Any_bad_address_rejects_the_whole_box(string text) =>
        Assert.False(CustomDohParser.TryParseAddresses(text, out _));

    [Fact]
    public void Literal_address_comes_from_ip_host()
    {
        Assert.Equal(IPAddress.Parse("9.9.9.9"), CustomDohParser.LiteralAddress(new Uri("https://9.9.9.9/dns-query")));
        Assert.Equal(IPAddress.Parse("2620:fe::fe"), CustomDohParser.LiteralAddress(new Uri("https://[2620:fe::fe]/dns-query")));
        Assert.Null(CustomDohParser.LiteralAddress(new Uri("https://dns.quad9.net/dns-query")));
    }

    [Fact]
    public void ToServer_prefers_manual_then_literal_then_resolved()
    {
        var manual = new CustomDoh { Endpoint = "https://9.9.9.9/q", ManualAddresses = ["1.1.1.1"], ResolvedAddresses = ["2.2.2.2"] };
        var literal = manual with { ManualAddresses = [] };
        var resolved = new CustomDoh { Endpoint = "https://dns.example.net/q", ResolvedAddresses = ["2.2.2.2"] };

        Assert.Equal("1.1.1.1", Assert.Single(CustomDohParser.ToServer(manual)!.BootstrapAddresses).ToString());
        Assert.Equal("9.9.9.9", Assert.Single(CustomDohParser.ToServer(literal)!.BootstrapAddresses).ToString());
        var server = CustomDohParser.ToServer(resolved)!;
        Assert.Equal("2.2.2.2", Assert.Single(server.BootstrapAddresses).ToString());
        Assert.Equal(CustomDohParser.ServerName, server.Name);
        Assert.Equal(new Uri("https://dns.example.net/q"), server.Endpoint);
    }

    [Fact]
    public void ToServer_is_null_without_valid_url_or_any_address()
    {
        Assert.Null(CustomDohParser.ToServer(null));
        Assert.Null(CustomDohParser.ToServer(new CustomDoh { Endpoint = "http://x/q", ResolvedAddresses = ["1.1.1.1"] }));
        Assert.Null(CustomDohParser.ToServer(new CustomDoh { Endpoint = "https://dns.example.net/q" }));
    }

    [Fact]
    public void ToServer_skips_hand_edited_garbage_and_nulls()
    {
        var custom = new CustomDoh { Endpoint = "https://dns.example.net/q", ManualAddresses = null!, ResolvedAddresses = ["x", "2.2.2.2"] };

        Assert.Equal("2.2.2.2", Assert.Single(CustomDohParser.ToServer(custom)!.BootstrapAddresses).ToString());
    }
}
