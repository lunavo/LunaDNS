using LunaDns.Core.Diagnostics;

namespace LunaDns.Core.Tests.Diagnostics;

public sealed class RedactorTests
{
    private static readonly Redactor Default = new("lunav", "DESKTOP-42");

    [Fact]
    public void Replaces_user_and_machine_case_insensitively() =>
        Assert.Equal(@"C:\Users\<user>\Desktop trên <host>", Default.Redact(@"C:\Users\LunaV\Desktop trên desktop-42"));

    [Fact]
    public void Leaves_longer_words_alone() =>
        Assert.Equal("annual pcs", new Redactor("ann", "pc").Redact("annual pcs"));

    [Fact]
    public void Masks_user_sid() =>
        Assert.Equal(@"HKU\<sid>\Software", Default.Redact(@"HKU\S-1-5-21-111-222-333-1001\Software"));

    [Theory]
    [InlineData("192.168.1.23", "192.168.x.x")]
    [InlineData("10.1.2.3", "10.1.x.x")]
    [InlineData("172.20.0.5", "172.20.x.x")]
    [InlineData("100.101.102.103", "100.101.x.x")]
    public void Masks_lan_ipv4(string input, string expected) =>
        Assert.Equal($"ip {expected}.", Default.Redact($"ip {input}."));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("127.0.0.1")]
    [InlineData("23.45.67.89")]
    [InlineData("172.32.0.1")]
    public void Keeps_public_and_loopback_ipv4(string input) =>
        Assert.Equal(input, Default.Redact(input));

    [Fact]
    public void Masks_ipv6_lan() =>
        Assert.Equal("<ipv6-lan> và <ipv6-lan>", Default.Redact("fd7a:115c:a1e0::53 và fe80::1"));

    [Fact]
    public void Keeps_version_numbers() =>
        Assert.Equal("Windows 10.0.19045.0", Default.Redact("Windows 10.0.19045.0"));

    [Fact]
    public void Blank_names_are_ignored() =>
        Assert.Equal("abc", new Redactor("", " ").Redact("abc"));
}
