using LunaDns.Platform.MacOS.Net;
using LunaDns.Platform.MacOS.Processes;

namespace LunaDns.Platform.MacOS.Tests;

public class MacNetworkIdentityTests
{
    private const string Route = """
           route to: default
        destination: default
               mask: default
            gateway: 192.168.1.1
          interface: en0
              flags: <UP,GATEWAY,DONE,STATIC,PRCLONING>
        """;

    [Fact]
    public void ParseRoute_ReadsGatewayAndInterface() => Assert.Equal(("192.168.1.1", "en0"), MacNetworkIdentity.ParseRoute(Route));

    [Fact]
    public void ParseRoute_NoGateway_Null() => Assert.Null(MacNetworkIdentity.ParseRoute("route: writing to routing socket: not in table"));

    [Theory]
    [InlineData("? (192.168.1.1) at 0:1a:2b:3:4:5 on en0 ifscope [ethernet]", "0:1a:2b:3:4:5")]
    [InlineData("? (192.168.1.1) at (incomplete) on en0 ifscope [ethernet]", null)]
    [InlineData("192.168.1.1 (192.168.1.1) -- no entry", null)]
    public void ParseArp(string output, string? mac) => Assert.Equal(mac, MacNetworkIdentity.ParseArp(output));

    [Fact]
    public void Current_UsesRouteThenArp()
    {
        var runner = new ScriptedRunner(new()
        {
            ["/sbin/route"] = new(0, Route),
            ["/usr/sbin/arp"] = new(0, "? (192.168.1.1) at 0:1a:2b:3:4:5 on en0 ifscope [ethernet]"),
        });
        var snapshot = new MacNetworkIdentity(runner).Current()!;
        Assert.False(snapshot.Id.Weak);
        Assert.Equal("en0 · 192.168.1.1", snapshot.Label);
    }

    [Fact]
    public void Current_RouteFails_Null()
    {
        var runner = new ScriptedRunner(new() { ["/sbin/route"] = new(1, "") });
        Assert.Null(new MacNetworkIdentity(runner).Current());
    }

    [Fact]
    public void Current_ArpThrows_WeakId()
    {
        var runner = new ScriptedRunner(new() { ["/sbin/route"] = new(0, Route) });
        Assert.True(new MacNetworkIdentity(runner).Current()!.Id.Weak);
    }

    private sealed class ScriptedRunner(Dictionary<string, ProcessResult> results) : IProcessRunner
    {
        public ProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            results.TryGetValue(fileName, out var r) ? r : throw new InvalidOperationException(fileName);
    }
}
