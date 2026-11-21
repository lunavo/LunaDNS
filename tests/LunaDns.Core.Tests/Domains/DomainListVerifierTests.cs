using System.Security.Cryptography;
using System.Text;
using LunaDns.Core.Domains;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListVerifierTests
{
    private static DomainListVerification Verify(byte[] file, long highest = 0) =>
        DomainListVerifier.Verify(file, Keys, highest);

    [Fact]
    public void Valid_file_is_accepted_with_groups()
    {
        var file = Signed(202611211402, new() { ["steam"] = ["steamcdn-a.akamaihd.net"], ["reddit"] = ["redditblog.com"] });

        var result = Verify(file);

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Equal(202611211402, result.Update!.Serial);
        Assert.Equal(["steamcdn-a.akamaihd.net"], result.Update.Groups[DomainGroup.Steam]);
        Assert.Equal(["redditblog.com"], result.Update.Groups[DomainGroup.Reddit]);
        Assert.Equal(file, result.Update.File);
        Assert.Equal(0, result.Dropped);
    }

    [Fact]
    public void Too_large_file_is_rejected_before_parsing() =>
        Assert.Equal(DomainListOutcome.TooLarge, Verify(new byte[DomainListVerifier.MaxBytes + 1]).Outcome);

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"keyId":"test","payload":"e30="}""")]
    [InlineData("""{"keyId":"test","payload":"%%%","sig":"AAAA"}""")]
    public void Broken_envelope_is_malformed(string text) =>
        Assert.Equal(DomainListOutcome.Malformed, Verify(Encoding.UTF8.GetBytes(text)).Outcome);

    [Fact]
    public void Unknown_key_id_is_rejected() =>
        Assert.Equal(DomainListOutcome.UnknownKey, Verify(Envelope("""{"format":1}""", keyId: "other")).Outcome);

    [Fact]
    public void Der_signature_is_malformed()
    {
        var payload = Encoding.UTF8.GetBytes("""{"format":1}""");
        var der = Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.Equal(DomainListOutcome.Malformed, Verify(Envelope("""{"format":1}""", signature: der)).Outcome);
    }

    [Fact]
    public void Changed_payload_fails_signature()
    {
        var payload = Encoding.UTF8.GetBytes("""{"format":1,"serial":1}""");
        var sig = Key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.Equal(DomainListOutcome.BadSignature, Verify(Envelope("""{"format":1,"serial":2}""", signature: sig)).Outcome);
    }

    [Fact]
    public void Signature_from_other_key_fails()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Encoding.UTF8.GetBytes("""{"format":1}""");
        var sig = other.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        Assert.Equal(DomainListOutcome.BadSignature, Verify(Envelope("""{"format":1}""", signature: sig)).Outcome);
    }

    [Fact]
    public void Newer_format_is_unknown() =>
        Assert.Equal(DomainListOutcome.UnknownFormat,
            Verify(Envelope("""{"format":2,"serial":5,"issued":"2026-11-21T00:00:00Z","groups":{}}""")).Outcome);

    [Theory]
    [InlineData("""{"format":1,"issued":"2026-11-21T00:00:00Z","groups":{}}""")]
    [InlineData("""{"format":1,"serial":5,"groups":{}}""")]
    [InlineData("""{"format":1,"serial":5,"issued":"2026-11-21T00:00:00Z"}""")]
    [InlineData("""{"format":1,"serial":5,"issued":"2026-11-21T00:00:00Z","groups":{"steam":null}}""")]
    [InlineData("""{"format":1,"serial":"5","issued":"2026-11-21T00:00:00Z","groups":{}}""")]
    public void Missing_or_wrong_fields_are_malformed(string payload) =>
        Assert.Equal(DomainListOutcome.Malformed, Verify(Envelope(payload)).Outcome);

    [Fact]
    public void Same_serial_is_not_newer_and_lower_is_downgrade()
    {
        var file = Signed(10, new() { ["steam"] = [] });
        Assert.Equal(DomainListOutcome.NotNewer, Verify(file, highest: 10).Outcome);
        Assert.Equal(DomainListOutcome.Downgrade, Verify(file, highest: 11).Outcome);
    }

    [Fact]
    public void Serial_check_can_be_skipped_when_reloading_store() =>
        Assert.Equal(DomainListOutcome.Accepted, DomainListVerifier.Verify(Signed(10, new()), Keys, 10, requireNewer: false).Outcome);

    [Fact]
    public void Group_over_limit_is_malformed()
    {
        var many = Enumerable.Range(0, DomainListVerifier.MaxPerGroup + 1).Select(i => $"cdn{i}.example.net").ToList();
        Assert.Equal(DomainListOutcome.Malformed, Verify(Signed(1, new() { ["steam"] = many })).Outcome);
    }

    [Fact]
    public void Total_over_limit_is_malformed()
    {
        List<string> Block(string g) => [.. Enumerable.Range(0, 200).Select(i => $"{g}{i}.example.net")];
        var groups = new Dictionary<string, List<string>>
        {
            ["steam"] = Block("a"), ["reddit"] = Block("b"), ["othergames"] = Block("c"), ["x1"] = Block("d"), ["x2"] = Block("e"),
            ["x3"] = ["f.example.net"],
        };
        Assert.Equal(DomainListOutcome.Malformed, Verify(Signed(1, groups)).Outcome);
    }

    [Fact]
    public void Bad_entries_are_dropped_not_the_file()
    {
        var file = Signed(1, new()
        {
            ["steam"] =
            [
                "good.example.net",
                "com",                    // hậu tố công cộng
                "easyanticheat.net",      // danh sách cấm
                "Upper.Example.net",      // chưa chuẩn hoá
                "steampowered.com",       // trùng lớp 1
                "store.steampowered.com", // nằm dưới lớp 1
                "good.example.net",       // trùng trong file
            ],
        });

        var result = Verify(file);

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Equal(["good.example.net"], result.Update!.Groups[DomainGroup.Steam]);
        Assert.Equal(6, result.Dropped);
    }

    [Fact]
    public void Pubg_custom_and_unknown_groups_are_ignored()
    {
        var file = Signed(1, new() { ["pubg"] = ["x.example.net"], ["custom"] = ["y.example.net"], ["epic"] = ["z.example.net"], ["othergames"] = ["w.example.net"] });

        var result = Verify(file);

        Assert.Equal([DomainGroup.OtherGames], result.Update!.Groups.Keys);
    }
}
