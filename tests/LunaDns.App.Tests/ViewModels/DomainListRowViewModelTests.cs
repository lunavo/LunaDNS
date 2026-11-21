using LunaDns.App.DomainList;
using LunaDns.App.ViewModels;
using LunaDns.Core.Domains;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.App.Tests.ViewModels;

public sealed class DomainListRowViewModelTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 7, 0, 0, TimeSpan.Zero));
    private DomainListState? _state = DomainListState.Empty(true);
    private DomainListCheck? _last;
    private DomainListCheck _nextCheck = new(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(DomainListOutcome.NotNewer));
    private readonly List<bool> _switched = [];
    private int _failures;

    private DomainListRowViewModel Create() => new(
        () => Task.FromResult(_state),
        on => { _switched.Add(on); _state = _state! with { Enabled = on }; return Task.FromResult(new DomainListOfferResult(DomainListOutcome.Accepted)); },
        _ => Task.FromResult(_last = _nextCheck),
        () => _last, () => false, () => _failures, () => false, _time);

    [Fact]
    public async Task Old_daemon_hides_the_row()
    {
        _state = null;
        var vm = Create();
        await vm.RefreshAsync();
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public async Task Never_downloaded_says_so()
    {
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.DomainListNone, vm.StatusText);
    }

    [Fact]
    public async Task Switched_off_says_builtin_list()
    {
        _state = DomainListState.Empty(false);
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.DomainListOff, vm.StatusText);
    }

    [Fact]
    public async Task Ok_state_shows_issue_date_and_check_time()
    {
        var local = _time.GetLocalNow();
        _state = new DomainListState(true, 5, local.AddDays(-1), local, new Dictionary<DomainGroup, IReadOnlyList<string>>());
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListOkFormat), local.AddDays(-1), local), vm.StatusText);
    }

    [Fact]
    public async Task Failed_check_after_success_keeps_old_version()
    {
        var local = _time.GetLocalNow();
        _state = new DomainListState(true, 5, local.AddDays(-1), local.AddHours(-30), new Dictionary<DomainGroup, IReadOnlyList<string>>());
        _last = new DomainListCheck(local.ToUniversalTime(), DomainListCheckKind.NetworkError);
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListFailedKeepFormat), local, local.AddDays(-1)), vm.StatusText);
    }

    [Theory]
    [InlineData(DomainListOutcome.NotNewer, nameof(Strings.DomainListUpToDate))]
    [InlineData(DomainListOutcome.BadSignature, nameof(Strings.DomainListBadSignature))]
    [InlineData(DomainListOutcome.UnknownFormat, nameof(Strings.DomainListNeedsNewerApp))]
    public async Task Check_now_shows_a_temporary_result(DomainListOutcome outcome, string key)
    {
        _nextCheck = new(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(outcome));
        var vm = Create();
        await vm.RefreshAsync();
        await vm.CheckNowCommand.ExecuteAsync();
        Assert.Equal((string)typeof(Strings).GetProperty(key)!.GetValue(null)!, vm.ResultText);
        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Null(vm.ResultText);
    }

    [Fact]
    public async Task Accepted_check_reports_count_and_raises_catalog_changed()
    {
        _nextCheck = new(DateTimeOffset.UnixEpoch, DomainListCheckKind.Offered, new(DomainListOutcome.Accepted, Added: 3));
        var vm = Create();
        var raised = 0;
        vm.CatalogChanged += (_, _) => raised++;
        await vm.RefreshAsync();
        await vm.CheckNowCommand.ExecuteAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListAddedFormat), 3), vm.ResultText);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Toggle_calls_the_service_and_raises_catalog_changed()
    {
        var vm = Create();
        var raised = 0;
        vm.CatalogChanged += (_, _) => raised++;
        await vm.RefreshAsync();
        await vm.ToggleCommand.ExecuteAsync();
        Assert.Equal([false], _switched);
        Assert.False(vm.IsEnabled);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Two_reapply_failures_show_the_red_line()
    {
        var local = _time.GetLocalNow();
        _state = new DomainListState(true, 5, local, local, new Dictionary<DomainGroup, IReadOnlyList<string>>());
        _failures = 2;
        var vm = Create();
        await vm.RefreshAsync();
        Assert.Equal(Strings.Format(nameof(Strings.DomainListReapplyFailedFormat), local), vm.StatusText);
        Assert.Equal(Tone.Error, vm.StatusTone);
    }

    [Fact]
    public async Task Check_now_is_disabled_while_switched_off()
    {
        _state = DomainListState.Empty(false);
        var vm = Create();
        await vm.RefreshAsync();
        Assert.False(vm.CheckNowCommand.CanExecute(null));
    }
}
