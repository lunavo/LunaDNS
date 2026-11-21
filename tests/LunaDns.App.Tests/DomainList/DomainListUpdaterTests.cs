using System.Net;
using LunaDns.App.DomainList;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.DomainList;

public sealed class DomainListUpdaterTests
{
    private static readonly Uri Url = DomainListUpdater.DefaultUrl("owner/repo");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 7, 0, 0, TimeSpan.Zero));
    private readonly FakeSink _sink = new();
    private readonly FakeHandler _http = new();

    private DomainListUpdater Create() => new(new HttpClient(_http), Url, _sink, _time, NullLogger.Instance);

    [Fact]
    public void Default_url_points_at_the_domain_list_release() =>
        Assert.Equal("https://github.com/owner/repo/releases/download/domain-list/domains.json", Url.ToString());

    [Fact]
    public async Task Never_checked_downloads_and_offers()
    {
        _http.Body = [1, 2, 3];
        var wait = await Create().TickAsync(CancellationToken.None);

        Assert.Equal([1, 2, 3], Assert.Single(_sink.Offered));
        Assert.Equal(DomainListUpdater.Interval, wait);
    }

    [Fact]
    public async Task Checked_recently_waits_until_due()
    {
        _sink.State = _sink.State! with { CheckedAt = _time.GetUtcNow() - TimeSpan.FromHours(20) };
        var wait = await Create().TickAsync(CancellationToken.None);

        Assert.Empty(_sink.Offered);
        Assert.Equal(TimeSpan.FromHours(4), wait);
    }

    [Fact]
    public async Task Disabled_switch_never_downloads()
    {
        _sink.State = DomainListState.Empty(enabled: false);
        await Create().TickAsync(CancellationToken.None);
        Assert.Equal(0, _http.Calls);
    }

    [Fact]
    public async Task Old_daemon_never_downloads()
    {
        _sink.State = null;
        Assert.Equal(DomainListUpdater.RetryDelay, await Create().TickAsync(CancellationToken.None));
        Assert.Equal(0, _http.Calls);
    }

    [Fact]
    public async Task Network_error_retries_in_an_hour_and_is_remembered()
    {
        _http.Status = HttpStatusCode.ServiceUnavailable;
        var updater = Create();

        Assert.Equal(DomainListUpdater.RetryDelay, await updater.TickAsync(CancellationToken.None));
        Assert.Equal(DomainListCheckKind.NetworkError, updater.LastCheck!.Kind);
        Assert.Empty(_sink.Offered);
    }

    [Fact]
    public async Task Oversized_body_is_cut_and_not_offered()
    {
        _http.Body = new byte[DomainListVerifier.MaxBytes + 1];
        var check = await Create().CheckNowAsync();
        Assert.Equal(DomainListCheckKind.TooLarge, check.Kind);
        Assert.Empty(_sink.Offered);
    }

    [Fact]
    public async Task Check_now_ignores_the_24_hour_mark()
    {
        _sink.State = _sink.State! with { CheckedAt = _time.GetUtcNow() };
        await Create().CheckNowAsync();
        Assert.Single(_sink.Offered);
    }

    [Theory]
    [InlineData(DomainListOutcome.Accepted, 0, 24)]
    [InlineData(DomainListOutcome.NotNewer, 0, 24)]
    [InlineData(DomainListOutcome.BadSignature, 0, 24)]
    [InlineData(DomainListOutcome.UnknownFormat, 0, 24)]
    [InlineData(DomainListOutcome.ReapplyFailed, 1, 1)]
    [InlineData(DomainListOutcome.ReapplyFailed, 2, 24)]
    [InlineData(DomainListOutcome.Unavailable, 0, 1)]
    public void Next_delay_follows_the_spec(DomainListOutcome outcome, int failures, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours),
            DomainListUpdater.NextDelay(new DomainListCheck(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(outcome)), failures));

    [Fact]
    public async Task Run_waits_30_seconds_before_the_first_check()
    {
        using var cts = new CancellationTokenSource();
        var run = Create().RunAsync(cts.Token);
        _time.Advance(TimeSpan.FromSeconds(29));
        await Task.Yield();
        Assert.Equal(0, _http.Calls);
        _time.Advance(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => _http.Calls == 1);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Điều kiện không thành sau 5 giây.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeSink : IDomainListSink
    {
        public DomainListState? State { get; set; } = DomainListState.Empty(enabled: true);
        public List<byte[]> Offered { get; } = [];
        public DomainListOutcome Next { get; set; } = DomainListOutcome.Accepted;
        public Task<DomainListState?> GetDomainListAsync() => Task.FromResult(State);
        public Task<DomainListOfferResult> OfferDomainListAsync(byte[] file) { Offered.Add(file); return Task.FromResult(new DomainListOfferResult(Next)); }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public byte[] Body { get; set; } = [];
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new ByteArrayContent(Body) });
        }
    }
}
