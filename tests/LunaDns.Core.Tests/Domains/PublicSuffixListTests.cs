using LunaDns.Core.Domains;

namespace LunaDns.Core.Tests.Domains;

public sealed class PublicSuffixListTests
{
    private const string Sample = """
        // comment
        com
        vn
        com.vn
        *.ck
        !www.ck
        // ===BEGIN PRIVATE DOMAINS===
        github.io
        公司.cn
        """;

    private static readonly PublicSuffixList List = PublicSuffixList.Parse(new StringReader(Sample));

    [Theory]
    [InlineData("com", true)]
    [InlineData("com.vn", true)]
    [InlineData("github.io", true)]
    [InlineData("foo.ck", true)]          // *.ck
    [InlineData("www.ck", false)]         // !www.ck
    [InlineData("anything", true)]        // luật ngầm "*": một nhãn luôn là hậu tố công cộng
    [InlineData("xn--55qx5d.cn", true)]   // 公司.cn đã chuyển punycode khi đọc
    [InlineData("reddit.com", false)]
    [InlineData("tuoitre.com.vn", false)]
    [InlineData("user.github.io", false)]
    public void Recognises_public_suffixes(string host, bool expected) =>
        Assert.Equal(expected, List.IsPublicSuffix(host));

    [Fact]
    public void Embedded_list_loads_and_knows_common_suffixes()
    {
        Assert.True(PublicSuffixList.Default.IsPublicSuffix("com"));
        Assert.True(PublicSuffixList.Default.IsPublicSuffix("com.vn"));
        Assert.True(PublicSuffixList.Default.IsPublicSuffix("github.io"));
        Assert.False(PublicSuffixList.Default.IsPublicSuffix("steampowered.com"));
    }
}
