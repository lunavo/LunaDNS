using LunaDns.Platform.MacOS.Proxy;

namespace LunaDns.Platform.MacOS.Tests;

public class AutoProxyOrphanSweepTests
{
    private const string Pac = "http://127.0.0.1:18083/proxy.pac";
    private static readonly HashSet<string> None = [];
    private readonly FakeNetworkSetup _ns = new();
    private readonly NetworkSetupAutoProxy _proxy;

    public AutoProxyOrphanSweepTests() => _proxy = new NetworkSetupAutoProxy(_ns);

    [Fact]
    public void Turns_off_services_still_using_a_lunadns_pac_the_journal_does_not_hold()
    {
        _ns.Auto["Luna Wifi"] = (Pac, true);
        _ns.Auto["Old USB"] = ("http://127.0.0.1:18090/proxy.pac", true); // service đang tắt vẫn quét

        Assert.Equal(2, _proxy.SweepOrphans(None));
        Assert.Equal((Pac, false), _ns.Auto["Luna Wifi"]);
        Assert.Equal(["state Luna Wifi off", "state Old USB off"], _ns.Changes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Keeps_services_whose_url_the_journal_still_holds()
    {
        _ns.Auto["Luna Wifi"] = (Pac, true);
        HashSet<string> covered = [Pac];

        Assert.Equal(0, _proxy.SweepOrphans(covered));
        Assert.Empty(_ns.Changes);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8099/canopi.pac")]      // PAC loopback của phần mềm khác
    [InlineData("http://127.0.0.1:8080/proxy.pac")]       // ngoài dải cổng của LunaDNS
    [InlineData("http://127.0.0.1:18091/proxy.pac")]
    [InlineData("http://localhost:18080/proxy.pac")]      // LunaDNS luôn ghi 127.0.0.1
    [InlineData("https://127.0.0.1:18080/proxy.pac")]
    [InlineData("http://127.0.0.1:18080/proxy.pac?x=1")]
    [InlineData("http://10.0.0.1:18080/proxy.pac")]
    public void Never_touches_foreign_pac(string url)
    {
        _ns.Auto["Luna Wifi"] = (url, true);

        Assert.Equal(0, _proxy.SweepOrphans(None));
        Assert.Empty(_ns.Changes);
    }

    [Fact]
    public void Already_disabled_lunadns_url_is_left_as_is()
    {
        _ns.Auto["Luna Wifi"] = (Pac, false);

        Assert.Equal(0, _proxy.SweepOrphans(None));
        Assert.Empty(_ns.Changes);
    }

    [Fact]
    public void Vpn_service_without_device_is_never_touched()
    {
        _ns.Auto["Surfshark. WireGuard"] = (Pac, true);

        Assert.Equal(0, _proxy.SweepOrphans(None));
    }
}
