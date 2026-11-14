using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Probing;

/// <summary>Kiểm tra trước khi can thiệp (spec 4.1): phân loại từng host để chỉ bật đúng biện pháp cần.</summary>
public sealed partial class Prober
{
    public static readonly TimeSpan DefaultCheckTimeout = TimeSpan.FromSeconds(3);

    private readonly IHostResolver _system;
    private readonly IHostResolver _doh;
    private readonly ITlsProbe _tls;
    private readonly TimeSpan _checkTimeout;
    private readonly ILogger _logger;

    public Prober(IHostResolver systemResolver, IHostResolver dohResolver, ITlsProbe tls, TimeSpan? checkTimeout = null, ILogger<Prober>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(systemResolver);
        ArgumentNullException.ThrowIfNull(dohResolver);
        ArgumentNullException.ThrowIfNull(tls);
        _system = systemResolver;
        _doh = dohResolver;
        _tls = tls;
        _checkTimeout = checkTimeout ?? DefaultCheckTimeout;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Kiểm tra các host song song. Kết quả giữ thứ tự của <paramref name="hosts"/>.</summary>
    public async Task<IReadOnlyList<ProbeResult>> RunAsync(IEnumerable<string> hosts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        return await Task.WhenAll(hosts.Select(host => ProbeAsync(host, cancellationToken))).ConfigureAwait(false);
    }

    public async Task<ProbeResult> ProbeAsync(string host, CancellationToken cancellationToken)
    {
        var systemTask = ResolveAsync(_system, host, cancellationToken);
        var dohTask = ResolveAsync(_doh, host, cancellationToken);
        var system = await systemTask.ConfigureAwait(false);
        var doh = await dohTask.ConfigureAwait(false);
        var blocked = system.Any(KnownBlockIps.IsBlockAddress);

        var verdict = await ClassifyAsync(host, system, doh, blocked, cancellationToken).ConfigureAwait(false);
        LogVerdict(_logger, host, verdict);
        return new ProbeResult(host, verdict, system, doh, blocked);
    }

    private async Task<ProbeVerdict> ClassifyAsync(
        string host, IReadOnlyList<IPAddress> system, IReadOnlyList<IPAddress> doh, bool blocked, CancellationToken cancellationToken)
    {
        if (doh.Count == 0) return ProbeVerdict.Unreachable;

        if (await HandshakeAsync(doh[0], host, split: false, cancellationToken).ConfigureAwait(false))
        {
            if (system.Count == 0 || blocked) return ProbeVerdict.DnsPoisoned;
            if (system.Any(address => doh.Contains(address))) return ProbeVerdict.Ok;
            // CDN hay trả IP khác nhau theo resolver, nên "khác IP" chưa phải là bị đầu độc.
            // IP của DNS hệ thống đưa được chứng chỉ hợp lệ cho host thì đó là máy chủ thật.
            return await HandshakeAsync(system[0], host, split: false, cancellationToken).ConfigureAwait(false)
                ? ProbeVerdict.Ok
                : ProbeVerdict.DnsPoisoned;
        }

        return await HandshakeAsync(doh[0], host, split: true, cancellationToken).ConfigureAwait(false)
            ? ProbeVerdict.SniFiltered
            : ProbeVerdict.Unreachable;
    }

    private async Task<IReadOnlyList<IPAddress>> ResolveAsync(IHostResolver resolver, string host, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        try
        {
            return await resolver.ResolveAsync(host, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogResolveFailed(_logger, host, ex);
            return [];
        }
        catch (Exception ex) when (ex is DohException or SocketException or IOException)
        {
            LogResolveFailed(_logger, host, ex);
            return [];
        }
    }

    private async Task<bool> HandshakeAsync(IPAddress address, string host, bool split, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        try
        {
            return await _tls.HandshakeAsync(address, host, split, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            LogHandshakeTimedOut(_logger, host, split, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Kiểm tra {Host}: {Verdict}")]
    private static partial void LogVerdict(ILogger logger, string host, ProbeVerdict verdict);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kiểm tra {Host}: một nguồn DNS không trả lời được")]
    private static partial void LogResolveFailed(ILogger logger, string host, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Kiểm tra {Host}: bắt tay TLS (cắt gói: {Split}) quá hạn")]
    private static partial void LogHandshakeTimedOut(ILogger logger, string host, bool split, Exception exception);
}
