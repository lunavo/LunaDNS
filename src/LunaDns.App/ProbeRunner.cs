using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>Dựng Prober theo settings hiện tại cho mỗi lần "Kiểm tra" (spec 4.1), rồi bỏ DohClient sau khi xong.</summary>
internal sealed class ProbeRunner(Func<AppSettings> settings, ILoggerFactory loggerFactory)
{
    public async Task<IReadOnlyList<ProbeResult>> RunAsync(IEnumerable<string> hosts, CancellationToken cancellationToken)
    {
        var current = settings();
        using var doh = DohClient.Create(current.DohServers(), loggerFactory.CreateLogger<DohClient>());
        var prober = new Prober(
            new SystemHostResolver(loggerFactory.CreateLogger<SystemHostResolver>()),
            new DohHostResolver(doh),
            new TlsProbe(current.Tuning().Split, current.Tuning().SplitDelay, loggerFactory.CreateLogger<TlsProbe>()),
            logger: loggerFactory.CreateLogger<Prober>());
        return await prober.RunAsync(hosts, cancellationToken);
    }

    public Task<IReadOnlyList<ProbeResult>> RunAsync(DomainSelection selection, CancellationToken cancellationToken) =>
        RunAsync(ProbeTargets.For(selection), cancellationToken);
}
