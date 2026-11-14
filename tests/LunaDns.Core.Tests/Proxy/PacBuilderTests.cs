using LunaDns.Core.Domains;
using LunaDns.Core.Proxy;

namespace LunaDns.Core.Tests.Proxy;

public class PacBuilderTests
{
    [Fact]
    public void Contains_proxy_directive_with_port_and_direct_fallback()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);

        Assert.Contains("function FindProxyForURL(url, host)", pac);
        Assert.Contains("\"PROXY 127.0.0.1:18080; DIRECT\"", pac);
        Assert.EndsWith("return \"DIRECT\";\n}\n", pac);
    }

    [Fact]
    public void Lists_every_domain_quoted()
    {
        var steam = GameDomains.For(DomainGroup.Steam);
        var pac = PacBuilder.Build(18080, steam);
        foreach (var d in steam) Assert.Contains($"\"{d}\"", pac);
    }

    [Fact]
    public void Uses_suffix_match_with_leading_dot()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);
        Assert.Contains("dnsDomainIs(host, \".\" + d[i])", pac);
        Assert.Contains("host == d[i]", pac);
    }

    [Theory]
    [InlineData("evil.com\"); alert(1); //")]
    [InlineData("Steam Powered.com")]
    [InlineData("")]
    public void Rejects_unsafe_domains(string bad) =>
        Assert.Throws<ArgumentException>(() => PacBuilder.Build(18080, [bad]));

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Rejects_invalid_port(int port) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PacBuilder.Build(port, ["a.com"]));

    [Fact]
    public void Sends_only_https_and_wss_through_proxy()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);

        Assert.Contains("if (url.substring(0, 6) != \"https:\" && url.substring(0, 4) != \"wss:\") return \"DIRECT\";", pac);
    }

    [Fact]
    public void Checks_scheme_before_matching_domains()
    {
        var pac = PacBuilder.Build(18080, ["steampowered.com"]);

        Assert.True(pac.IndexOf("url.substring(0, 6)", StringComparison.Ordinal) < pac.IndexOf("var d = [", StringComparison.Ordinal));
    }
}
