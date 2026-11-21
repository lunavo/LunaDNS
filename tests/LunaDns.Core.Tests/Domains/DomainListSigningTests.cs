using System.Text;
using LunaDns.Core.Domains;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListSigningTests
{
    [Fact]
    public void Signed_file_verifies_with_its_public_key() =>
        Assert.Equal(DomainListOutcome.Accepted, DomainListVerifier.Verify(Signed(1, new() { ["steam"] = ["cdn.example.net"] }), Keys, 0).Outcome);

    [Fact]
    public void Valid_source_has_no_errors() =>
        Assert.Empty(DomainListSigning.CheckSource(Encoding.UTF8.GetBytes("""{"groups":{"steam":["cdn.example.net"],"reddit":[]}}""")));

    [Theory]
    [InlineData("""{"groups":{"steam":["com"]}}""", "steam: com")]
    [InlineData("""{"groups":{"steam":["steampowered.com"]}}""", "steam: steampowered.com")]
    [InlineData("""{"groups":{"epic":["x.example.net"]}}""", "epic")]
    [InlineData("""{"groups":{"pubg":["x.example.net"]}}""", "pubg")]
    [InlineData("not json", "JSON")]
    public void Bad_source_reports_the_offending_entry(string json, string expected) =>
        Assert.Contains(DomainListSigning.CheckSource(Encoding.UTF8.GetBytes(json)), e => e.Contains(expected, StringComparison.Ordinal));
}
