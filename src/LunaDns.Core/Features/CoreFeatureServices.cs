using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Features;

/// <summary>
/// Dựng dịch vụ thật. Mỗi dịch vụ có DohClient riêng, tạo theo <paramref name="dohServers"/> lúc mở (lựa chọn DoH ở
/// mục Nâng cao có hiệu lực từ lần bật kế tiếp) và huỷ cùng dịch vụ. Proxy và listener PUBG phân giải máy chủ qua DoH,
/// không qua DNS hệ thống (KH3 quyết định 5, KH5 quyết định 4). Endpoint chỉ đổi trong test.
/// </summary>
public sealed class CoreFeatureServices(
    Func<IReadOnlyList<DohServer>> dohServers,
    ILoggerFactory? loggerFactory = null,
    IPEndPoint? dnsEndPoint = null,
    IPEndPoint? pubgEndPoint = null) : IFeatureServices
{
    private readonly ILoggerFactory _loggers = loggerFactory ?? NullLoggerFactory.Instance;

    public IRunningService StartDnsStub(IReadOnlyList<string> domains)
    {
        var doh = CreateDoh();
        try
        {
            var handler = new DnsQueryHandler(domains, doh, new DnsCache(), _loggers.CreateLogger<DnsQueryHandler>());
            var stub = DohStubServer.Start(dnsEndPoint ?? DohStubServer.DefaultEndPoint, handler, _loggers.CreateLogger<DohStubServer>());
            return new RunningService(stub.Completion, stub.DisposeAsync, doh);
        }
        catch
        {
            doh.Dispose();
            throw;
        }
    }

    public IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var doh = CreateDoh();
        try
        {
            var options = new SplitProxyOptions
            {
                Domains = domains,
                FirstPort = port ?? SplitProxyOptions.DefaultFirstPort,
                LastPort = port ?? SplitProxyOptions.DefaultLastPort,
                Split = tuning.Split,
                SplitDelay = tuning.SplitDelay,
            };
            var proxy = SplitProxy.Start(options, Connector(doh), _loggers.CreateLogger<SplitProxy>());
            return new RunningProxy(proxy, doh);
        }
        catch
        {
            doh.Dispose();
            throw;
        }
    }

    public IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        var doh = CreateDoh();
        try
        {
            var options = new SniForwarderOptions { Domains = domains, Split = tuning.Split, SplitDelay = tuning.SplitDelay };
            var forwarder = SniForwarder.Start(pubgEndPoint ?? SniForwarder.DefaultEndPoint, options, Connector(doh), _loggers.CreateLogger<SniForwarder>());
            return new RunningService(forwarder.Completion, forwarder.DisposeAsync, doh);
        }
        catch
        {
            doh.Dispose();
            throw;
        }
    }

    private DohClient CreateDoh() => DohClient.Create(dohServers(), _loggers.CreateLogger<DohClient>());

    private static TcpUpstreamConnector Connector(DohClient doh) => new(new DohHostResolver(doh, new DnsCache()));

    private sealed class RunningService(Task completion, Func<ValueTask> stop, DohClient doh) : IRunningService
    {
        public Task Completion => completion;

        public DohActivity? Doh => doh.Activity;

        public async ValueTask DisposeAsync()
        {
            await stop().ConfigureAwait(false);
            doh.Dispose();
        }
    }

    private sealed class RunningProxy(SplitProxy proxy, DohClient doh) : IRunningProxy
    {
        public Task Completion => proxy.Completion;

        public Uri PacUrl => proxy.PacUrl;

        public int Port => proxy.Port;

        public int ActiveConnections => proxy.ActiveConnections;

        public async ValueTask DisposeAsync()
        {
            await proxy.DisposeAsync().ConfigureAwait(false);
            doh.Dispose();
        }
    }
}
