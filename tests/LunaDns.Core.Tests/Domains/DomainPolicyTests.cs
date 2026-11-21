// tests/LunaDns.Core.Tests/Domains/DomainPolicyTests.cs
using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainPolicyTests
{
    [Theory]
    [InlineData("  Reddit.COM ", "reddit.com")]
    [InlineData("https://www.reddit.com/r/vietnam?x=1#top", "reddit.com")]
    [InlineData("http://user@old.reddit.com:8080/path", "old.reddit.com")]
    [InlineData("*.redd.it", "redd.it")]
    [InlineData("reddit.com.", "reddit.com")]
    [InlineData("tiếngviệt.vn", "xn--tingvit-5t4cyc.vn")]
    public void Normalize_cleans_what_people_paste(string raw, string expected) =>
        Assert.Equal(expected, DomainPolicy.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("# chú thích")]
    public void Normalize_skips_blank_and_comment_lines(string raw) =>
        Assert.Null(DomainPolicy.Normalize(raw));

    [Theory]
    [InlineData("reddit.com", DomainRejection.None)]
    [InlineData("i.redd.it", DomainRejection.None)]
    [InlineData("a..b.com", DomainRejection.Syntax)]
    [InlineData("-bad.com", DomainRejection.Syntax)]
    [InlineData("bad_.com", DomainRejection.Syntax)]
    [InlineData("1.2.3.4", DomainRejection.Syntax)]
    [InlineData("com", DomainRejection.LocalName)]          // một nhãn
    [InlineData("com.vn", DomainRejection.PublicSuffix)]
    [InlineData("github.io", DomainRejection.PublicSuffix)]
    [InlineData("printer.local", DomainRejection.LocalName)]
    [InlineData("nas.home.arpa", DomainRejection.LocalName)]
    [InlineData("router.lan", DomainRejection.LocalName)]
    [InlineData("dns.google", DomainRejection.Denied)]
    [InlineData("cloudflare-dns.com", DomainRejection.Denied)]
    [InlineData("www.msftconnecttest.com", DomainRejection.Denied)]   // tên miền con của mục cấm
    [InlineData("cdn.xenuine.example.com", DomainRejection.Denied)]   // nhãn chứa "xenuine"
    [InlineData("pubg.com", DomainRejection.Denied)]                  // tên miền cha của accounts.pubg.com
    public void Validate_follows_spec_table(string host, DomainRejection expected) =>
        Assert.Equal(expected, DomainPolicy.Validate(host));

    [Fact]
    public void Validate_rejects_too_long_names()
    {
        var label = new string('a', 64);
        Assert.Equal(DomainRejection.Syntax, DomainPolicy.Validate(label + ".com"));
        var longName = string.Join('.', Enumerable.Repeat(new string('a', 60), 5)) + ".com";
        Assert.Equal(DomainRejection.Syntax, DomainPolicy.Validate(longName));
    }

    [Fact]
    public void Routable_catalog_groups_never_hit_the_policy()
    {
        foreach (var group in DomainCatalog.Selectable)
            foreach (var domain in DomainCatalog.For(group))
                Assert.True(DomainPolicy.IsRoutable(domain), domain);
    }

    [Fact]
    public void Split_input_accepts_newlines_commas_and_spaces() =>
        Assert.Equal(["a.com", "b.com", "c.com", "d.com"], DomainPolicy.SplitInput("a.com\r\nb.com, c.com\t d.com\n\n"));

    [Fact]
    public void Split_input_keeps_comment_lines_out()
    {
        var parts = DomainPolicy.SplitInput("# danh sách\nreddit.com # chính\nredd.it");
        Assert.Equal(["reddit.com", "redd.it"], parts);
    }

    [Fact]
    public void Check_reports_duplicates_in_custom_and_in_enabled_groups()
    {
        string[] custom = ["reddit.com"];
        string[] catalog = ["steampowered.com"];

        Assert.Equal(DomainRejection.Duplicate, DomainPolicy.Check("REDDIT.com", custom, catalog).Rejection);
        var inGroup = DomainPolicy.Check("store.steampowered.com", custom, catalog);
        Assert.Equal(DomainRejection.Duplicate, inGroup.Rejection);
        Assert.Equal("steampowered.com", inGroup.Detail);
        Assert.True(DomainPolicy.Check("redd.it", custom, catalog).Ok);
    }

    [Fact]
    public void Check_enforces_the_limit()
    {
        var full = Enumerable.Range(0, DomainPolicy.MaxCustomDomains).Select(i => $"d{i}.example.org").ToArray();
        Assert.Equal(DomainRejection.Limit, DomainPolicy.Check("new.example.org", full, []).Rejection);
    }

    [Fact]
    public void Check_explains_denied_entries()
    {
        var check = DomainPolicy.Check("dns.google", [], []);
        Assert.Equal(DomainRejection.Denied, check.Rejection);
        Assert.False(string.IsNullOrWhiteSpace(check.Detail));
    }
}
