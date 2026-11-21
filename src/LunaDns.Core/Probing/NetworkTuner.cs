using LunaDns.Core.Dns;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Probing;

/// <summary>
/// D3 "Tìm cấu hình hợp mạng": chọn máy DoH nhanh nhất, phân loại các host bằng Prober, rồi thử lần lượt các tổ hợp cắt gói
/// (TuneCandidates) tới khi mọi host bắt tay được. Không tự lưu hay bật gì: người gọi áp TuneResult.
/// </summary>
public sealed partial class NetworkTuner
{
    private readonly Func<IReadOnlyList<DohChoice>, CancellationToken, Task<IReadOnlyList<DohLatency>>> _measureDoh;
    private readonly IHostResolver _system;
    private readonly Func<DohServer, IHostResolver> _dohResolver;
    private readonly Func<TuneCandidate, ITlsProbe> _tls;
    private readonly TimeSpan _checkTimeout;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;

    /// <param name="dohResolver">Resolver chỉ gồm một máy DoH (không rơi sang máy dự phòng); người gọi giữ và giải phóng.</param>
    public NetworkTuner(
        Func<IReadOnlyList<DohChoice>, CancellationToken, Task<IReadOnlyList<DohLatency>>> measureDoh,
        IHostResolver systemResolver,
        Func<DohServer, IHostResolver> dohResolver,
        Func<TuneCandidate, ITlsProbe> tls,
        TimeSpan? checkTimeout = null,
        ILoggerFactory? loggers = null)
    {
        ArgumentNullException.ThrowIfNull(measureDoh);
        ArgumentNullException.ThrowIfNull(systemResolver);
        ArgumentNullException.ThrowIfNull(dohResolver);
        ArgumentNullException.ThrowIfNull(tls);
        _measureDoh = measureDoh;
        _system = systemResolver;
        _dohResolver = dohResolver;
        _tls = tls;
        _checkTimeout = checkTimeout ?? Prober.DefaultCheckTimeout;
        _loggers = loggers ?? NullLoggerFactory.Instance;
        _logger = _loggers.CreateLogger<NetworkTuner>();
    }

    public async Task<TuneResult> RunAsync(
        IReadOnlyList<string> hosts, IReadOnlyList<DohChoice> choices, TuneCandidate current,
        IProgress<TuneProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(current);

        progress?.Report(new TuneProgress(TuneStage.MeasuringDoh, 0, 0, null));
        var latencies = await _measureDoh(choices, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var best = latencies.FirstOrDefault(l => l.Median is not null);
        var choice = best is null ? null : choices.FirstOrDefault(c => c.Provider == best.Provider);
        if (choice is null)
        {
            LogDohFailed(_logger);
            return new TuneResult(TuneOutcome.DohFailed, null, null, ProbeRemedies.None, [], []);
        }
        LogDohChosen(_logger, choice.Provider);

        progress?.Report(new TuneProgress(TuneStage.Classifying, 0, 0, current));
        var prober = new Prober(_system, _dohResolver(choice.Server), _tls(current), _checkTimeout, _loggers.CreateLogger<Prober>());
        var probe = await prober.RunAsync(hosts, cancellationToken).ConfigureAwait(false);
        if (probe.All(r => r.Verdict is ProbeVerdict.Ok or ProbeVerdict.DnsPoisoned))
            return new TuneResult(TuneOutcome.SplitNotNeeded, choice.Provider, current, ProbeAdvisor.Recommend(probe), probe, []);

        var candidates = TuneCandidates.Ordered(current);
        IReadOnlyList<string> fewest = [.. probe.Select(r => r.Host)];
        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            progress?.Report(new TuneProgress(TuneStage.TryingSplit, i + 1, candidates.Count, candidate));
            var failing = await FailingHostsAsync(probe, candidate, cancellationToken).ConfigureAwait(false);
            LogCandidate(_logger, candidate, failing.Count);
            if (failing.Count == 0)
                return new TuneResult(TuneOutcome.Found, choice.Provider, candidate,
                    ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, [.. probe.Select(Helped)], []);
            if (failing.Count < fewest.Count) fewest = failing;
        }
        return new TuneResult(TuneOutcome.NothingWorked, choice.Provider, null, ProbeRemedies.None, probe, fewest);
    }

    private async Task<IReadOnlyList<string>> FailingHostsAsync(
        IReadOnlyList<ProbeResult> probe, TuneCandidate candidate, CancellationToken cancellationToken)
    {
        var tls = _tls(candidate);
        var passed = await Task.WhenAll(probe.Select(r => PassesAsync(tls, r, cancellationToken))).ConfigureAwait(false);
        return [.. probe.Where((_, i) => !passed[i]).Select(r => r.Host)];
    }

    private async Task<bool> PassesAsync(ITlsProbe tls, ProbeResult result, CancellationToken cancellationToken)
    {
        if (result.DohAddresses.Count == 0) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_checkTimeout);
        try
        {
            return await tls.HandshakeAsync(result.DohAddresses[0], result.Host, split: true, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <summary>Bắt tay nguyên vẹn lỗi nhưng tổ hợp tìm được qua: đó là lọc SNI, không phải "không vào được".</summary>
    private static ProbeResult Helped(ProbeResult result) =>
        result.Verdict == ProbeVerdict.Unreachable && result.DohAddresses.Count > 0
            ? result with { Verdict = ProbeVerdict.SniFiltered }
            : result;

    [LoggerMessage(Level = LogLevel.Information, Message = "Dò cấu hình: không máy DoH nào trả lời")]
    private static partial void LogDohFailed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dò cấu hình: dùng DoH {Provider}")]
    private static partial void LogDohChosen(ILogger logger, DohProvider provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dò cấu hình: {Candidate} còn {Failing} host lỗi")]
    private static partial void LogCandidate(ILogger logger, TuneCandidate candidate, int failing);
}
