using System.Net;
using LunaDns.Core.Probing;

namespace LunaDns.Core.Tests.Probing;

public class KnownBlockIpsTests
{
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("169.254.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::ffff:10.0.0.1")]
    public void Flags_addresses_no_public_game_server_can_have(string address) =>
        Assert.True(KnownBlockIps.IsBlockAddress(IPAddress.Parse(address)));

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("23.50.60.70")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.0")]
    [InlineData("172.32.0.1")]
    [InlineData("103.10.124.5")]
    [InlineData("2606:4700::1111")]
    public void Leaves_public_addresses_alone(string address) =>
        Assert.False(KnownBlockIps.IsBlockAddress(IPAddress.Parse(address)));
}
