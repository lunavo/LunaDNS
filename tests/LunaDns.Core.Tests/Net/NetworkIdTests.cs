using LunaDns.Core.Net;

namespace LunaDns.Core.Tests.Net;

public class NetworkIdTests
{
    [Theory]
    [InlineData("AA-BB-CC-01-02-03", "aa:bb:cc:01:02:03")]
    [InlineData("0:1a:2b:3:4:5", "00:1a:2b:03:04:05")]
    [InlineData("00:00:00:00:00:00", null)]
    [InlineData("ff:ff:ff:ff:ff:ff", null)]
    [InlineData("(incomplete)", null)]
    [InlineData(null, null)]
    public void NormalizeMac(string? raw, string? expected) => Assert.Equal(expected, NetworkId.NormalizeMac(raw));

    [Fact]
    public void SameMacAndKind_SameId()
    {
        var a = NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Wireless80211", "Wi-Fi")!;
        var b = NetworkId.From("AA-BB-CC-01-02-03", "10.0.0.1", "Wireless80211", "Wi-Fi 2")!;
        Assert.Equal(a, b);
        Assert.False(a.Weak);
        Assert.Equal(64, a.Value.Length);
        Assert.Equal(a.Value[..8], a.Short);
    }

    [Fact]
    public void DifferentKind_DifferentId() =>
        Assert.NotEqual(NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Ethernet", "Ethernet"),
            NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Wireless80211", "Wi-Fi"));

    [Fact]
    public void NoMac_FallsBackToGatewayIp_Weak()
    {
        var id = NetworkId.From(null, "10.64.0.1", "Ppp", "Cellular")!;
        Assert.True(id.Weak);
        Assert.Equal(id, NetworkId.From("00:00:00:00:00:00", "10.64.0.1", "Other", "Cellular"));
    }

    [Fact]
    public void NoGateway_Null() => Assert.Null(NetworkId.From("aa:bb:cc:01:02:03", null, "Ethernet", "Ethernet"));
}
