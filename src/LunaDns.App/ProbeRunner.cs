using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>Dựng Prober theo settings hiện tại cho mỗi lần "Kiểm tra" (spec 4.1), rồi bỏ DohClient sau khi xong.</summary>
internal sealed class ProbeRunner(Func<AppSettings> settings, ILoggerFactory loggerFactory)
{
    public async Task<IReadOnlyList<ProbeResult>> RunAsync(CancellationToken cancellationToken)
    {
        var current = settings();
        using var doh = DohClient.Create(current.DohServers(), loggerFactory.CreateLogger<DohClient>());
        var prober = new Prober(
            new SystemHostResolver(loggerFactory.CreateLogger<SystemHostResolver>()),
            new DohHostResolver(doh),
            new TlsProbe(current.SplitMode, loggerFactory.CreateLogger<TlsProbe>()),
            logger: loggerFactory.CreateLogger<Prober>());
        // KH6 U0: PUBG ra khỏi v1, chỉ kiểm nhóm Steam; settings.CheckPubg bị bỏ qua.
        return await prober.RunAsync(ProbeTargets.For(includePubg: false), cancellationToken);
    }
}
