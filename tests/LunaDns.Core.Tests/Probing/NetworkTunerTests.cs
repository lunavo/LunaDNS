using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Probing;
using LunaDns.Core.Tests.Dns;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Probing;

public class NetworkTunerTests
{
    private static readonly string[] Hosts = ["a.example", "b.example", "c.example", "d.example"];
    private static readonly string[] CdHosts = ["c.example", "d.example"];
    private static readonly string[] DHosts = ["d.example"];
    private static readonly int[] FirstTwo = [1, 2];
    private static readonly IPAddress Real = IPAddress.Parse("23.50.60.70");
    private static readonly TuneCandidate Current = TuneCandidate.Default;

    private static readonly IReadOnlyList<DohChoice> Choices =
    [
        new(DohProvider.Cloudflare, DohServer.Cloudflare),
        new(DohProvider.Quad9, DohCatalog.Get(DohProvider.Quad9)),
    ];

    private readonly FakeResolver _system = new() { Respond = _ => [Real] };
    private readonly FakeResolver _doh = new() { Respond = _ => [Real] };
    private readonly List<(TuneCandidate Candidate, string Host, bool Split)> _calls = [];
    private readonly List<DohServer> _dohServers = [];

    /// <summary>Bắt tay nguyên vẹn (split=false) theo IntactOk; cắt gói theo Passes(tổ hợp, host).</summary>
    private Func<string, bool> IntactOk { get; set; } = _ => true;

    private Func<TuneCandidate, string, bool> Passes { get; set; } = (_, _) => true;

    private IReadOnlyList<DohLatency> Latencies { get; set; } =
    [
        new(DohProvider.Quad9, TimeSpan.FromMilliseconds(12), null),
        new(DohProvider.Cloudflare, TimeSpan.FromMilliseconds(30), null),
    ];

    private NetworkTuner Create() => new(
        (_, _) => Task.FromResult(Latencies),
        _system,
        server =>
        {
            _dohServers.Add(server);
            return _doh;
        },
        candidate => new FakeTlsProbe
        {
            RespondAsync = (_, host, split, _) =>
            {
                lock (_calls) _calls.Add((candidate, host, split));
                return Task.FromResult(split ? Passes(candidate, host) : IntactOk(host));
            },
        },
        checkTimeout: TimeSpan.FromSeconds(1));

    private Task<TuneResult> Run(IProgress<TuneProgress>? progress = null, CancellationToken token = default) =>
        Create().RunAsync(Hosts, Choices, Current, progress, token);

    private int SplitCalls(TuneCandidate candidate)
    {
        lock (_calls) return _calls.Count(c => c.Split && c.Candidate == candidate);
    }

    [Fact]
    public async Task Picks_fastest_doh_and_skips_split_when_nothing_is_filtered()
    {
        var result = await Run();

        Assert.Equal(TuneOutcome.SplitNotNeeded, result.Outcome);
        Assert.Equal(DohProvider.Quad9, result.Doh);
        Assert.Equal(Current, result.Candidate);
        Assert.Equal(ProbeRemedies.None, result.Remedies);
        Assert.Equal(DohCatalog.Get(DohProvider.Quad9), Assert.Single(_dohServers));
        Assert.Equal(4, result.Probe.Count);
    }

    [Fact]
    public async Task Poisoned_dns_only_recommends_dns_routing()
    {
        _system.Respond = _ => []; // DNS hệ thống không trả, DoH bắt tay được: Prober kết luận DnsPoisoned

        var result = await Run();

        Assert.Equal(TuneOutcome.SplitNotNeeded, result.Outcome);
        Assert.Equal(ProbeRemedies.DnsRouting, result.Remedies);
    }

    [Fact]
    public async Task All_doh_failing_stops_before_probing()
    {
        Latencies = [new(DohProvider.Quad9, null, "lỗi"), new(DohProvider.Cloudflare, null, "lỗi")];

        var result = await Run();

        Assert.Equal(TuneOutcome.DohFailed, result.Outcome);
        Assert.Null(result.Doh);
        Assert.Empty(result.Probe);
        Assert.Empty(_system.Hosts);
    }

    [Fact]
    public async Task Stops_at_first_candidate_that_passes_every_host()
    {
        IntactOk = _ => false; // mọi host bị lọc SNI
        var winner = TuneCandidates.Table[2]; // giữa SNI, 0 ms
        Passes = (candidate, _) => candidate == winner;

        var result = await Run();

        Assert.Equal(TuneOutcome.Found, result.Outcome);
        Assert.Equal(winner, result.Candidate);
        Assert.Equal(ProbeRemedies.DnsRouting | ProbeRemedies.SniProxy, result.Remedies);
        Assert.Equal(0, SplitCalls(TuneCandidates.Table[3])); // không thử tổ hợp sau tổ hợp thắng
        Assert.Equal(4, SplitCalls(winner));
    }

    [Fact]
    public async Task One_failing_host_fails_the_candidate()
    {
        IntactOk = _ => false;
        Passes = (candidate, host) => host != "d.example" || candidate == TuneCandidates.Table[7];

        var result = await Run();

        Assert.Equal(TuneOutcome.Found, result.Outcome);
        Assert.Equal(TuneCandidates.Table[7], result.Candidate);
    }

    [Fact]
    public async Task Found_marks_unreachable_hosts_as_sni_filtered()
    {
        IntactOk = _ => false;
        // Tổ hợp đang dùng không qua: Prober kết luận Unreachable; tổ hợp thứ hai qua hết.
        Passes = (candidate, _) => candidate != Current;

        var result = await Run();

        Assert.Equal(TuneOutcome.Found, result.Outcome);
        Assert.All(result.Probe, r => Assert.Equal(ProbeVerdict.SniFiltered, r.Verdict));
    }

    [Fact]
    public async Task Nothing_working_reports_hosts_of_best_candidate()
    {
        IntactOk = _ => false;
        Passes = (candidate, host) => candidate == TuneCandidates.Table[4] && host is "a.example" or "b.example";

        var result = await Run();

        Assert.Equal(TuneOutcome.NothingWorked, result.Outcome);
        Assert.Null(result.Candidate);
        Assert.Equal(CdHosts, result.FailingHosts);
        Assert.Equal(ProbeRemedies.None, result.Remedies);
    }

    [Fact]
    public async Task Host_without_doh_address_never_passes()
    {
        _doh.Respond = host => host == "d.example" ? [] : [Real];
        IntactOk = _ => false;

        var result = await Run();

        Assert.Equal(TuneOutcome.NothingWorked, result.Outcome);
        Assert.Equal(DHosts, result.FailingHosts);
    }

    [Fact]
    public async Task Cancellation_throws_instead_of_returning_a_result()
    {
        IntactOk = _ => false;
        using var cts = new CancellationTokenSource();
        var tuner = new NetworkTuner(
            (_, _) => Task.FromResult(Latencies), _system, _ => _doh,
            _ => new FakeTlsProbe
            {
                RespondAsync = async (_, _, split, token) =>
                {
                    if (!split) return false;
                    await cts.CancelAsync();
                    await Task.Delay(Timeout.Infinite, token);
                    return true;
                },
            },
            checkTimeout: TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tuner.RunAsync(Hosts, Choices, Current, null, cts.Token));
    }

    [Fact]
    public async Task Progress_reports_stages_in_order()
    {
        IntactOk = _ => false;
        Passes = (candidate, _) => candidate == TuneCandidates.Table[1];
        var seen = new List<TuneProgress>();
        var progress = new SyncProgress(seen.Add);

        await Run(progress);

        Assert.Equal(TuneStage.MeasuringDoh, seen[0].Stage);
        Assert.Equal(TuneStage.Classifying, seen[1].Stage);
        Assert.Equal(FirstTwo, seen.Skip(2).Select(p => p.Index));
        Assert.All(seen.Skip(2), p => Assert.Equal(TuneCandidates.MaxCount, p.Total));
    }

    /// <summary>Progress&lt;T&gt; gọi qua SynchronizationContext; test cần gọi ngay.</summary>
    private sealed class SyncProgress(Action<TuneProgress> report) : IProgress<TuneProgress>
    {
        public void Report(TuneProgress value) => report(value);
    }
}
