using LunaDns.Core.Net;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Net;

public sealed class StaggeredRaceTests
{
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(500);
    private readonly FakeTimeProvider _time = new();

    /// <summary>Mỗi lượt là một TaskCompletionSource do test điều khiển; token của lượt được ghi lại.</summary>
    private sealed class Attempts
    {
        public List<TaskCompletionSource<string>> Sources { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<string> Start(int index, CancellationToken token)
        {
            var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Sources)
            {
                Sources.Add(source);
                Tokens.Add(token);
            }
            return source.Task;
        }

        public int Count
        {
            get { lock (Sources) return Sources.Count; }
        }
    }

    private Task<RaceResult<string>> Run(Attempts attempts, int count = 3,
        List<(int, Exception)>? failures = null, List<string>? discarded = null, CancellationToken token = default) =>
        StaggeredRace.RunAsync(count, Stagger, _time, attempts.Start, ex => ex is IOException, token,
            failures is null ? null : (i, ex) => { lock (failures) failures.Add((i, ex)); },
            discarded is null ? null : value => { lock (discarded) discarded.Add(value); });

    [Fact]
    public async Task First_attempt_wins_without_starting_the_next()
    {
        var attempts = new Attempts();
        var race = Run(attempts);

        attempts.Sources[0].SetResult("a");
        var result = await race;

        Assert.True(result.Succeeded);
        Assert.Equal("a", result.Value);
        Assert.Equal(0, result.Winner);
        _time.Advance(Stagger * 4);
        Assert.Equal(1, attempts.Count);
    }

    [Fact]
    public async Task Slow_attempt_gets_company_after_stagger_and_loses()
    {
        var attempts = new Attempts();
        var race = Run(attempts);
        Assert.Equal(1, attempts.Count);

        _time.Advance(Stagger - TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, attempts.Count);
        _time.Advance(TimeSpan.FromMilliseconds(1));
        await Eventually.TrueAsync(() => attempts.Count == 2);

        attempts.Sources[1].SetResult("b");
        var result = await race;

        Assert.Equal(("b", 1), (result.Value, result.Winner));
        Assert.True(attempts.Tokens[0].IsCancellationRequested);
        Assert.Equal(2, attempts.Count);
    }

    [Fact]
    public async Task Failure_starts_the_next_attempt_at_once()
    {
        var attempts = new Attempts();
        var failures = new List<(int, Exception)>();
        var race = Run(attempts, failures: failures);

        attempts.Sources[0].SetException(new IOException("chết"));
        await Eventually.TrueAsync(() => attempts.Count == 2);
        attempts.Sources[1].SetResult("b");

        Assert.Equal(1, (await race).Winner);
        var (index, error) = Assert.Single(failures);
        Assert.Equal(0, index);
        Assert.IsType<IOException>(error);
    }

    [Fact]
    public async Task Every_attempt_failing_returns_failures_in_order()
    {
        var attempts = new Attempts();
        var race = Run(attempts, count: 2);

        attempts.Sources[0].SetException(new IOException("0"));
        await Eventually.TrueAsync(() => attempts.Count == 2);
        attempts.Sources[1].SetException(new IOException("1"));
        var result = await race;

        Assert.False(result.Succeeded);
        Assert.Equal(["0", "1"], result.Failures.Select(e => e.Message));
    }

    [Fact]
    public async Task Late_success_of_a_loser_is_discarded()
    {
        var attempts = new Attempts();
        var discarded = new List<string>();
        var race = Run(attempts, discarded: discarded);
        _time.Advance(Stagger);
        await Eventually.TrueAsync(() => attempts.Count == 2);

        attempts.Sources[1].SetResult("b");
        await race;
        attempts.Sources[0].SetResult("a"); // lượt thua bỏ qua huỷ, vẫn trả kết quả

        await Eventually.TrueAsync(() => { lock (discarded) return discarded.Count == 1; });
        Assert.Equal("a", discarded[0]);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var attempts = new Attempts();
        using var cts = new CancellationTokenSource();
        var race = Run(attempts, token: cts.Token);

        await cts.CancelAsync();
        attempts.Sources[0].SetCanceled(attempts.Tokens[0]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => race);
    }

    [Fact]
    public async Task Unexpected_exception_is_rethrown_and_others_cancelled()
    {
        var attempts = new Attempts();
        var race = Run(attempts);
        _time.Advance(Stagger);
        await Eventually.TrueAsync(() => attempts.Count == 2);

        attempts.Sources[1].SetException(new InvalidOperationException("lỗi lập trình"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => race);
        Assert.True(attempts.Tokens[0].IsCancellationRequested);
    }

    [Fact]
    public async Task Synchronous_throw_from_attempt_counts_as_failure()
    {
        var calls = 0;
        var result = await StaggeredRace.RunAsync(2, Stagger, _time,
            (i, _) => ++calls == 1 ? throw new IOException("đồng bộ") : Task.FromResult("b"),
            ex => ex is IOException, CancellationToken.None);

        Assert.Equal(1, result.Winner);
    }
}
