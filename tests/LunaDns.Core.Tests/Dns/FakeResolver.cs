using System.Net;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Tests.Dns;

internal sealed class FakeResolver : IHostResolver
{
    private readonly List<string> _hosts = [];

    public Func<string, IReadOnlyList<IPAddress>> Respond { get; set; } = _ => [IPAddress.Loopback];

    public TimeSpan Delay { get; set; }

    public IReadOnlyList<string> Hosts
    {
        get
        {
            lock (_hosts) return [.. _hosts];
        }
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
    {
        lock (_hosts) _hosts.Add(host);
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
        return Respond(host);
    }
}
