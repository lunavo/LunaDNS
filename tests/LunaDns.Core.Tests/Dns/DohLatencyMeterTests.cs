using LunaDns.Core.Dns;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Dns;

public sealed class DohLatencyMeterTests
{
    private sealed class SteppingTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(int ms) => Interlocked.Add(ref _ticks, TimeSpan.FromMilliseconds(ms).Ticks);
    }

    /// <summary>Lần gọi thứ i đẩy đồng hồ <c>delaysMs[i]</c>; giá trị âm thì ném DohException.</summary>
    private sealed class ScriptedUpstream(SteppingTime time, int[] delaysMs, byte[]? response = null) : IDnsUpstream, IDisposable
    {
        private int _calls;
        public bool Disposed { get; private set; }

        public Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
        {
            var delay = delaysMs[Math.Min(_calls++, delaysMs.Length - 1)];
            if (delay < 0) throw new DohException("hỏng");
            time.Advance(delay);
            return Task.FromResult(response ?? TestDnsMessage.Response(DohLatencyMeter.ProbeHost, 0, 300));
        }

        public void Dispose() => Disposed = true;
    }

    private static DohChoice Choice(DohProvider provider) => new(provider, DohCatalog.Get(provider));

    [Fact]
    public async Task Ranks_by_median_of_measured_queries_ignoring_warm_up()
    {
        var time = new SteppingTime();
        var upstreams = new Dictionary<string, ScriptedUpstream>
        {
            ["Cloudflare"] = new(time, [500, 30, 10, 40]), // khởi động chậm không tính; trung vị 30
            ["Google"] = new(time, [5, 20, 25, 90]),       // trung vị 25
            ["Quad9"] = new(time, [5, 50, 50, 50]),        // trung vị 50
        };
        var meter = new DohLatencyMeter(s => upstreams[s.Name], time);

        var results = await meter.MeasureAsync([Choice(DohProvider.Cloudflare), Choice(DohProvider.Google), Choice(DohProvider.Quad9)], CancellationToken.None);

        Assert.Equal([DohProvider.Google, DohProvider.Cloudflare, DohProvider.Quad9], results.Select(r => r.Provider));
        Assert.Equal(TimeSpan.FromMilliseconds(25), results[0].Median);
        Assert.Equal(TimeSpan.FromMilliseconds(30), results[1].Median);
        Assert.All(results, r => Assert.Null(r.Error));
        Assert.All(upstreams.Values, u => Assert.True(u.Disposed));
    }

    [Fact]
    public async Task Failures_rank_last_with_error()
    {
        var time = new SteppingTime();
        var servFail = DnsWire.BuildHeaderOnlyResponse(TestDnsMessage.Query(DohLatencyMeter.ProbeHost), DnsRcode.ServFail);
        var noAnswer = TestDnsMessage.Response(DohLatencyMeter.ProbeHost, 0);
        var upstreams = new Dictionary<string, ScriptedUpstream>
        {
            ["Cloudflare"] = new(time, [1, 1, -1]),
            ["Google"] = new(time, [1], servFail),
            ["Quad9"] = new(time, [1], noAnswer),
            ["AdGuard"] = new(time, [1, 70]),
        };
        var meter = new DohLatencyMeter(s => upstreams[s.Name], time);

        var results = await meter.MeasureAsync(DohCatalog.BuiltIn, CancellationToken.None);

        Assert.Equal(DohProvider.AdGuard, results[0].Provider);
        Assert.Equal(TimeSpan.FromMilliseconds(70), results[0].Median);
        Assert.Equal([DohProvider.Cloudflare, DohProvider.Google, DohProvider.Quad9], results.Skip(1).Select(r => r.Provider));
        Assert.All(results.Skip(1), r => { Assert.Null(r.Median); Assert.False(string.IsNullOrEmpty(r.Error)); });
    }

    [Fact]
    public async Task Cancellation_stops_measuring()
    {
        using var cts = new CancellationTokenSource();
        var meter = new DohLatencyMeter(_ => new HangingUpstream());

        var task = meter.MeasureAsync([Choice(DohProvider.Cloudflare)], cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Overall_timeout_keeps_finished_results_and_marks_the_rest_failed()
    {
        var time = new FakeTimeProvider();
        var meter = new DohLatencyMeter(s => s.Name == "Google" ? new HangingUpstream() : new InstantUpstream(), time);

        var task = meter.MeasureAsync([Choice(DohProvider.Cloudflare), Choice(DohProvider.Google)], TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.False(task.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(30));
        var results = await task;

        Assert.Equal([DohProvider.Cloudflare, DohProvider.Google], results.Select(r => r.Provider));
        Assert.Equal(TimeSpan.Zero, results[0].Median);
        Assert.Null(results[0].Error);
        Assert.Null(results[1].Median);
        Assert.False(string.IsNullOrEmpty(results[1].Error));
    }

    [Fact]
    public async Task Caller_cancellation_still_throws_when_a_timeout_is_set()
    {
        using var cts = new CancellationTokenSource();
        var meter = new DohLatencyMeter(_ => new HangingUpstream(), new FakeTimeProvider());

        var task = meter.MeasureAsync([Choice(DohProvider.Cloudflare)], TimeSpan.FromSeconds(30), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    private sealed class InstantUpstream : IDnsUpstream
    {
        public Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken) =>
            Task.FromResult(TestDnsMessage.Response(DohLatencyMeter.ProbeHost, 0, 300));
    }

    private sealed class HangingUpstream : IDnsUpstream
    {
        public async Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return [];
        }
    }
}
