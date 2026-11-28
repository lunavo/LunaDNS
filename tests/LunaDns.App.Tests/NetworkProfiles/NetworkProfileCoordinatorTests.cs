using LunaDns.App.Features;
using LunaDns.App.NetworkProfiles;
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Net;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.NetworkProfiles;

public sealed class NetworkProfileCoordinatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lunadns-coord-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeHost _host = new();
    private readonly NetworkProfileStore _store;
    private readonly NetworkProfileCoordinator _coordinator;

    private static readonly NetworkSnapshot Home = new(NetworkId.From("aa:bb:cc:01:02:03", "192.168.1.1", "Wireless80211", "Wi-Fi")!, "Wi-Fi · 192.168.1.1");
    private static readonly NetworkSnapshot Phone = new(NetworkId.From("aa:bb:cc:09:09:09", "172.20.10.1", "Wireless80211", "Wi-Fi")!, "Wi-Fi · 172.20.10.1");
    private static readonly TuneCandidate Mid = new(new HelloSplit(SplitMode.MidSni, Records: true), 10);

    public NetworkProfileCoordinatorTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new NetworkProfileStore(Path.Combine(_dir, "profiles.json"));
        _coordinator = new NetworkProfileCoordinator(_store, _host, _time, NullLogger.Instance);
        _host.Coordinator = _coordinator;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static IReadOnlyList<ProbeResult> All(ProbeVerdict verdict) =>
        [.. ProbeTargets.Steam.Select(h => new ProbeResult(h, verdict, [], [], false))];

    private void Save(NetworkSnapshot net, DohProvider doh, TuneCandidate candidate, bool stale = false) =>
        _store.Upsert(new NetworkProfile(net.Id.Value, net.Id.Weak, net.Label, doh, candidate, _time.GetUtcNow(), _time.GetUtcNow(), stale));

    [Fact]
    public async Task KnownNetwork_DifferentTuning_Applies()
    {
        Save(Home, DohProvider.Google, Mid);
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Equal((DohProvider.Google, Mid), Assert.Single(_host.Applied));
        Assert.Empty(_host.ProbeCalls);
        Assert.Contains("đã dùng cấu hình đã lưu", _coordinator.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KnownNetwork_SameTuning_OnlyTouches()
    {
        Save(Home, DohProvider.Cloudflare, TuneCandidate.Default);
        _time.Advance(TimeSpan.FromHours(1));
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Empty(_host.Applied);
        Assert.Equal(_time.GetUtcNow(), _store.Find(Home.Id.Value)!.LastSeen);
    }

    [Fact]
    public async Task NewNetwork_Passes_Saves()
    {
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Phone);
        var saved = _store.Find(Phone.Id.Value)!;
        Assert.Equal(DohProvider.Cloudflare, saved.Doh);
        Assert.Equal(TuneCandidate.Default, saved.Candidate);
        Assert.False(_coordinator.ShowBlocked);
    }

    [Fact]
    public async Task NewNetwork_Blocked_Banner_NoSave()
    {
        _host.NextProbe = All(ProbeVerdict.Unreachable);
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Null(_store.Find(Phone.Id.Value));
        Assert.True(_coordinator.ShowBlocked);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SniFiltered_PassesOnlyWithProxy(bool proxyOn, bool expected) =>
        Assert.Equal(expected, NetworkProfileCoordinator.Passes(All(ProbeVerdict.SniFiltered), proxyOn));

    [Fact]
    public async Task Busy_SkipsThenRunsWhenBusyEnds()
    {
        _host.Busy = true;
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Empty(_host.ProbeCalls);
        _host.Busy = false;
        await _coordinator.OnBusyEndedAsync();
        Assert.Single(_host.ProbeCalls);
        await _coordinator.OnBusyEndedAsync();
        Assert.Single(_host.ProbeCalls); // cùng mạng, đã xử lý
    }

    [Fact]
    public async Task ChangeDuringProbe_CancelsOld_NoSave()
    {
        var hold = new TaskCompletionSource();
        _host.HoldProbe = hold;
        _host.NextProbe = All(ProbeVerdict.Ok);
        var first = _coordinator.OnNetworkChangedAsync(Phone);
        _host.Busy = true; // lượt đầu đang trong RunBusyAsync
        await _coordinator.OnNetworkChangedAsync(Home);
        hold.SetResult();
        await first;
        Assert.True(_host.ProbeCalls[0].IsCancellationRequested);
        Assert.Null(_store.Find(Phone.Id.Value));
    }

    [Fact]
    public async Task ApplyFails_ShowsError_KeepsProfile()
    {
        Save(Home, DohProvider.Google, Mid);
        _host.ApplyResult = new TuningApplyResult(false, Strings.DaemonNeedsUpdate);
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Equal(Strings.DaemonNeedsUpdate, Assert.Single(_host.Errors));
        Assert.NotNull(_store.Find(Home.Id.Value));
    }

    [Fact]
    public async Task Off_OrNoFeature_DoesNothing()
    {
        Save(Home, DohProvider.Google, Mid);
        _host.Remember = false;
        await _coordinator.OnNetworkChangedAsync(Home);
        _host.Remember = true;
        _host.Status = new FeatureStatus(false, false, false, null, 0);
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Empty(_host.Applied);
        Assert.Empty(_host.ProbeCalls);
    }

    [Fact]
    public async Task ManualCheckBlocked_OnKnownNetwork_MarksStale_NextChangeProbes()
    {
        Save(Home, DohProvider.Google, Mid);
        await _coordinator.OnNetworkChangedAsync(Home);
        await _coordinator.OnCheckCompletedAsync(All(ProbeVerdict.Unreachable));
        Assert.True(_store.Find(Home.Id.Value)!.Stale);
        Assert.True(_coordinator.ShowBlocked);
        await _coordinator.OnNetworkChangedAsync(Phone);
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Single(_host.ProbeCalls.Skip(1)); // Home stale: Kiểm tra thay vì áp
        Assert.False(_store.Find(Home.Id.Value)!.Stale);
    }

    [Fact]
    public async Task ChangeWhileUserCheckRuns_CheckResultCounts_NoSecondProbe()
    {
        _host.Busy = true;
        await _coordinator.OnNetworkChangedAsync(Phone); // lúc mở app: Kiểm tra đang chạy
        await _coordinator.OnCheckCompletedAsync(All(ProbeVerdict.Unreachable));
        _host.Busy = false;
        await _coordinator.OnBusyEndedAsync();
        Assert.Empty(_host.ProbeCalls);
        Assert.True(_coordinator.ShowBlocked);
        Assert.False(_coordinator.HasProfile);
        _coordinator.OnTuneSucceeded(DohProvider.Quad9, Mid);
        Assert.True(_coordinator.HasProfile);
    }

    [Fact]
    public async Task TuneSucceeded_SavesForCurrentNetwork_Forget_Clear()
    {
        await _coordinator.OnNetworkChangedAsync(null);
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Home);
        _coordinator.OnTuneSucceeded(DohProvider.Quad9, Mid);
        Assert.Equal(DohProvider.Quad9, _store.Find(Home.Id.Value)!.Doh);
        _coordinator.Forget();
        Assert.Null(_store.Find(Home.Id.Value));
        Save(Phone, DohProvider.Google, Mid);
        _coordinator.ClearAll();
        Assert.Empty(_store.All);
    }

    [Fact]
    public async Task StoreWriteFailure_DoesNotThrow()
    {
        var bad = Path.Combine(_dir, "bad");
        Directory.CreateDirectory(Path.Combine(bad, "profiles.json")); // đường dẫn file là một thư mục
        var coordinator = new NetworkProfileCoordinator(new NetworkProfileStore(Path.Combine(bad, "profiles.json")), _host, _time, NullLogger.Instance);
        _host.NextProbe = All(ProbeVerdict.Ok);
        await coordinator.OnNetworkChangedAsync(Phone);
        coordinator.OnTuneSucceeded(DohProvider.Quad9, Mid);
        coordinator.Forget();
        coordinator.ClearAll();
        await coordinator.OnCheckCompletedAsync(All(ProbeVerdict.Unreachable));
        Assert.Single(_host.ProbeCalls); // luồng vẫn chạy tiếp dù ghi file lỗi
        Assert.True(coordinator.ShowBlocked);
    }

    [Fact]
    public async Task BusyChangeAwayAndBack_RerunsOriginalNetwork()
    {
        var hold = new TaskCompletionSource();
        _host.HoldProbe = hold;
        _host.NextProbe = All(ProbeVerdict.Ok);
        var first = _coordinator.OnNetworkChangedAsync(Phone);
        _host.Busy = true;
        await _coordinator.OnNetworkChangedAsync(Home);
        await _coordinator.OnNetworkChangedAsync(Phone);
        hold.SetResult();
        await first; // lượt đầu bị huỷ; hết bận thì chạy lại Phone (như MainViewModel gọi OnBusyEndedAsync)
        Assert.True(_host.ProbeCalls[0].IsCancellationRequested);
        Assert.Equal(2, _host.ProbeCalls.Count);
        Assert.NotNull(_store.Find(Phone.Id.Value));
    }

    [Fact]
    public async Task ProbeThrows_DoesNotThrow_NextChangeWorks()
    {
        _host.ProbeException = new InvalidOperationException("boom");
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Null(_store.Find(Phone.Id.Value));
        _host.ProbeException = null;
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Home);
        Assert.Equal(2, _host.ProbeCalls.Count);
        Assert.NotNull(_store.Find(Home.Id.Value));
        _host.ProbeException = new InvalidOperationException("boom");
        await _coordinator.OnNetworkChangedAsync(Phone);
        _host.ProbeException = null;
        await _coordinator.OnBusyEndedAsync(); // lượt lỗi không thử lại liên tục; chờ lần đổi mạng sau
        Assert.Equal(3, _host.ProbeCalls.Count);
    }

    [Fact]
    public async Task BusyEndedDuringOwnRun_DoesNotRerun()
    {
        _host.NextProbe = All(ProbeVerdict.Ok);
        await _coordinator.OnNetworkChangedAsync(Phone);
        Assert.Single(_host.ProbeCalls);
    }

    [Fact]
    public async Task FailedRun_NotRetriedUntilNextChange()
    {
        _host.ProbeException = new InvalidOperationException("boom");
        await _coordinator.OnNetworkChangedAsync(Phone);
        await _coordinator.OnBusyEndedAsync();
        Assert.Single(_host.ProbeCalls);
    }

    private sealed class FakeHost : INetworkProfileHost
    {
        public NetworkProfileCoordinator? Coordinator { get; set; }
        public bool Remember { get; set; } = true;
        public bool Busy { get; set; }
        public FeatureStatus Status { get; set; } = new(true, true, false, 18080, 0);
        public TuningState Tuning { get; set; } = new(DohProvider.Cloudflare, TuneCandidate.Default);
        public TuningApplyResult ApplyResult { get; set; } = new(true, null);
        public IReadOnlyList<ProbeResult> NextProbe { get; set; } = [];
        public TaskCompletionSource? HoldProbe { get; set; }
        public Exception? ProbeException { get; set; }
        public List<(DohProvider, TuneCandidate)> Applied { get; } = [];
        public List<CancellationToken> ProbeCalls { get; } = [];
        public List<string> Errors { get; } = [];

        public bool RememberPerNetwork => Remember;
        public bool IsBusy => Busy;
        public Task<TuningState?> GetTuningAsync() => Task.FromResult<TuningState?>(Tuning);

        public Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)
        {
            Applied.Add((doh, candidate));
            if (ApplyResult.Success) Tuning = new TuningState(doh, candidate);
            return Task.FromResult(ApplyResult);
        }

        public async Task<IReadOnlyList<ProbeResult>> ProbeSteamAsync(CancellationToken cancellationToken)
        {
            ProbeCalls.Add(cancellationToken);
            if (HoldProbe is { } hold) { HoldProbe = null; await hold.Task; }
            if (ProbeException is { } ex) throw ex;
            return NextProbe;
        }

        public async Task RunBusyAsync(Func<Task> work)
        {
            Busy = true;
            try { await work(); }
            finally { Busy = false; }
            if (Coordinator is { } c) await c.OnBusyEndedAsync(); // như MainViewModel: báo hết bận trước khi lượt chạy tiếp
        }

        public void ShowError(string message) => Errors.Add(message);
    }
}
