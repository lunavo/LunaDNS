using LunaDns.Core.Platform;
using LunaDns.Platform.MacOS.Proxy;

namespace LunaDns.Platform.MacOS.Tests;

public class NetworkSetupAutoProxyTests
{
    private static readonly Uri Pac = new("http://127.0.0.1:18080/proxy.pac");
    private readonly FakeNetworkSetup _ns = new();
    private readonly NetworkSetupAutoProxy _proxy;

    public NetworkSetupAutoProxyTests() => _proxy = new NetworkSetupAutoProxy(_ns);

    [Fact]
    public void Lists_enabled_services_with_a_device_only()
    {
        Assert.Equal(["Luna Ethernet", "Luna Wifi"], NetworkSetupAutoProxy.ParseServiceOrder(FakeNetworkSetup.ServiceOrder, includeDisabled: false));
        Assert.Equal(["Luna Ethernet", "Luna Wifi", "Old USB"], NetworkSetupAutoProxy.ParseServiceOrder(FakeNetworkSetup.ServiceOrder, includeDisabled: true));
    }

    [Theory]
    [InlineData("URL: (null)\nEnabled: No\n", null, false)]
    [InlineData("URL: http://a/b.pac\nEnabled: Yes\n", "http://a/b.pac", true)]
    public void Parses_auto_proxy(string output, string? url, bool enabled) =>
        Assert.Equal((url, enabled), NetworkSetupAutoProxy.ParseAutoProxy(output));

    [Fact]
    public void Apply_then_revert_restores_every_service_exactly()
    {
        var before = new Dictionary<string, (string?, bool)>(_ns.Auto);

        var snapshot = _proxy.Plan(Pac, allowOverride: false);
        _proxy.Apply(snapshot);

        Assert.Equal((Pac.AbsoluteUri, true), _ns.Auto["Luna Wifi"]);
        Assert.Equal((Pac.AbsoluteUri, true), _ns.Auto["Luna Ethernet"]);
        Assert.Equal((null, false), _ns.Auto["Surfshark. WireGuard"]);

        _proxy.Revert(snapshot);

        Assert.Equal(before["Luna Wifi"], _ns.Auto["Luna Wifi"]);
        // networksetup không xoá được URL: service vốn trống chỉ bị tắt lại.
        Assert.Equal((Pac.AbsoluteUri, false), _ns.Auto["Luna Ethernet"]);
    }

    [Fact]
    public void Apply_twice_issues_no_second_change()
    {
        var snapshot = _proxy.Plan(Pac, allowOverride: false);
        _proxy.Apply(snapshot);
        var count = _ns.Changes.Count;
        _proxy.Apply(snapshot);

        Assert.Equal(count, _ns.Changes.Count);
    }

    [Fact]
    public void Existing_enabled_pac_or_manual_proxy_needs_override()
    {
        _ns.Auto["Luna Wifi"] = ("http://corp/proxy.pac", true);
        var ex = Assert.Throws<ConfiguratorException>(() => _proxy.Plan(Pac, allowOverride: false));
        Assert.Equal(ConfiguratorError.ExistingProxyConfig, ex.Error);

        _ns.Auto["Luna Wifi"] = (null, false);
        _ns.ManualProxy.Add("Luna Ethernet");
        Assert.Throws<ConfiguratorException>(() => _proxy.Plan(Pac, allowOverride: false));

        var snapshot = _proxy.Plan(Pac, allowOverride: true);
        Assert.Equal(2, snapshot.Services!.Count);
    }

    [Fact]
    public void Revert_restores_previously_enabled_pac()
    {
        _ns.Auto["Luna Wifi"] = ("http://corp/proxy.pac", true);
        var snapshot = _proxy.Plan(Pac, allowOverride: true);
        _proxy.Apply(snapshot);
        _proxy.Revert(snapshot);

        Assert.Equal(("http://corp/proxy.pac", true), _ns.Auto["Luna Wifi"]);
    }

    [Fact]
    public void Revert_keeps_a_service_the_user_changed_since()
    {
        var snapshot = _proxy.Plan(Pac, allowOverride: false);
        _proxy.Apply(snapshot);
        _ns.Auto["Luna Wifi"] = ("http://user/new.pac", true);

        _proxy.Revert(snapshot);

        Assert.Equal(("http://user/new.pac", true), _ns.Auto["Luna Wifi"]);
    }

    [Fact]
    public void Windows_snapshot_is_refused()
    {
        var windows = new AutoProxySnapshot("S-1-5-21-1", false, null, false, Pac.AbsoluteUri);
        Assert.Throws<ArgumentException>(() => _proxy.Revert(windows));
    }

    [Fact]
    public void Error_output_becomes_configurator_exception()
    {
        var failing = new NetworkSetupAutoProxy(new FailingRunner());
        var ex = Assert.Throws<ConfiguratorException>(() => failing.Plan(Pac, allowOverride: false));
        Assert.Equal(ConfiguratorError.AccessDenied, ex.Error);
    }

    private sealed class FailingRunner : Processes.IProcessRunner
    {
        public Processes.ProcessResult Run(string fileName, IReadOnlyList<string> arguments) =>
            new(14, "** Error: Command requires admin privileges.");
    }
}
