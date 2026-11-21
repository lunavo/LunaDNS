using LunaDns.Core.Domains;
using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

public sealed class CoreFeatureServicesTests
{
    private static readonly IPEndPoint AnyLoopbackPort = new(IPAddress.Loopback, 0);

    private static CoreFeatureServices Create() =>
        new(() => DohServer.Defaults, dnsEndPoint: AnyLoopbackPort, pubgEndPoint: AnyLoopbackPort);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Dns_stub_runs_until_disposed()
    {
        var stub = Create().StartDnsStub(FeatureDomains.Routed(new DomainSelection()));
        Assert.False(stub.Completion.IsCompleted);

        await stub.DisposeAsync();

        await stub.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Sni_proxy_on_fixed_port_uses_exactly_that_port()
    {
        var port = FreePort();

        await using var proxy = Create().StartSniProxy(FeatureDomains.Routed(new DomainSelection()), port, FeatureTuning.Default);

        Assert.Equal(port, proxy.Port);
        Assert.Equal(new Uri($"http://127.0.0.1:{port}/proxy.pac"), proxy.PacUrl);
        Assert.Equal(0, proxy.ActiveConnections);
    }

    [Fact]
    public async Task Sni_proxy_without_port_binds_in_default_range()
    {
        await using var proxy = Create().StartSniProxy(FeatureDomains.Routed(new DomainSelection()), null, FeatureTuning.Default);

        Assert.InRange(proxy.Port, 18080, 18090);
    }

    [Fact]
    public async Task Pubg_forwarder_runs_until_disposed()
    {
        var forwarder = Create().StartPubgForwarder(FeatureDomains.Pubg, FeatureTuning.Default);
        Assert.False(forwarder.Completion.IsCompleted);

        await forwarder.DisposeAsync();

        await forwarder.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
