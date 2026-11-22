using LunaDns.Core.Dns;
using LunaDns.Core.Features;

namespace LunaDns.Core.Tests.Features;

internal class FakeRunningService : IRunningService
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _completion.Task;

    public bool Disposed { get; private set; }

    public DohActivity? Doh { get; set; }

    /// <summary>Chạy trong DisposeAsync, để test ghi thứ tự "hoàn tác trước, dừng sau".</summary>
    public Action? OnDispose { get; set; }

    /// <summary>Giả lập socket chết giữa chừng: Completion faulted.</summary>
    public void Crash() => _completion.TrySetException(new IOException("giả lập socket chết"));

    public ValueTask DisposeAsync()
    {
        if (!Disposed) OnDispose?.Invoke();
        Disposed = true;
        _completion.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeRunningProxy(int port) : FakeRunningService, IRunningProxy
{
    public Uri PacUrl { get; } = new($"http://127.0.0.1:{port}/proxy.pac");

    public int Port { get; } = port;

    public int ActiveConnections { get; set; }
}

internal sealed class FakeFeatureServices : IFeatureServices
{
    public const int DefaultProxyPort = 18080;

    private readonly object _gate = new();
    private readonly List<(Feature Feature, FakeRunningService Service)> _started = [];
    private readonly List<int?> _proxyPorts = [];
    private readonly Dictionary<Feature, Exception> _failStart = [];
    private readonly HashSet<Feature> _failOnce = [];

    public IReadOnlyList<string>? LastDomains { get; private set; }

    public FeatureTuning? LastTuning { get; private set; }

    public IReadOnlyList<int?> ProxyPorts
    {
        get { lock (_gate) return [.. _proxyPorts]; }
    }

    /// <summary>
    /// Từ giờ mọi lần mở dịch vụ của <paramref name="feature"/> đều ném <paramref name="error"/>;
    /// <paramref name="once"/> = true thì chỉ lần mở kế tiếp.
    /// </summary>
    public void FailStart(Feature feature, Exception error, bool once = false)
    {
        lock (_gate)
        {
            _failStart[feature] = error;
            if (once) _failOnce.Add(feature);
            else _failOnce.Remove(feature);
        }
    }

    public int Starts(Feature feature)
    {
        lock (_gate) return _started.Count(s => s.Feature == feature);
    }

    public IReadOnlyList<FakeRunningService> All(Feature feature)
    {
        lock (_gate) return [.. _started.Where(s => s.Feature == feature).Select(s => s.Service)];
    }

    public FakeRunningService Latest(Feature feature) => All(feature)[^1];

    public IRunningService StartDnsStub(IReadOnlyList<string> domains) =>
        Add(Feature.DnsRouting, domains, null, new FakeRunningService());

    public IRunningProxy StartSniProxy(IReadOnlyList<string> domains, int? port, FeatureTuning tuning)
    {
        lock (_gate) _proxyPorts.Add(port);
        return (FakeRunningProxy)Add(Feature.SniProxy, domains, tuning, new FakeRunningProxy(port ?? DefaultProxyPort));
    }

    public IRunningService StartPubgForwarder(IReadOnlyList<string> domains, FeatureTuning tuning) =>
        Add(Feature.PubgMode, domains, tuning, new FakeRunningService());

    private FakeRunningService Add(Feature feature, IReadOnlyList<string> domains, FeatureTuning? tuning, FakeRunningService service)
    {
        lock (_gate)
        {
            if (_failStart.TryGetValue(feature, out var error))
            {
                if (_failOnce.Remove(feature)) _failStart.Remove(feature);
                throw error;
            }
            LastDomains = domains;
            LastTuning = tuning;
            _started.Add((feature, service));
            return service;
        }
    }
}
