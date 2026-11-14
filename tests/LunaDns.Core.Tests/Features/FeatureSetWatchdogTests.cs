using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Journal;
using LunaDns.Core.Tests.Platform;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureSetWatchdogTests : IAsyncLifetime
{
    private static readonly int?[] SamePortTwice = [null, FakeFeatureServices.DefaultProxyPort];

    private readonly FakeTimeProvider _time = new();
    private readonly FakeConfigurator _configurator = new();
    private readonly FakeFeatureServices _services = new();
    private readonly ChangeJournal _journal;
    private readonly FeatureSet _features;

    public FeatureSetWatchdogTests()
    {
        _journal = new ChangeJournal(new InMemoryJournalStore(), SystemChanges.Reverters(_configurator));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, _configurator), _services, time: _time);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _features.DisposeAsync();

    private TaskCompletionSource<FeatureStoppedEventArgs> ObserveStopped()
    {
        var stopped = new TaskCompletionSource<FeatureStoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        _features.FeatureStopped += (_, e) => stopped.TrySetResult(e);
        return stopped;
    }

    private async Task CrashAndWaitForRestartAsync(Feature feature)
    {
        var before = _services.Starts(feature);
        _services.Latest(feature).Crash();
        await Eventually.TrueAsync(() => _services.Starts(feature) == before + 1);
    }

    [Fact]
    public async Task Crashed_service_is_restarted_without_new_journal_entries()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        await CrashAndWaitForRestartAsync(Feature.DnsRouting);

        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.True(_services.All(Feature.DnsRouting)[0].Disposed);
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Proxy_restart_reuses_the_same_port()
    {
        await _features.EnableAsync(Feature.SniProxy);

        await CrashAndWaitForRestartAsync(Feature.SniProxy);

        Assert.Equal(SamePortTwice, _services.ProxyPorts);
    }

    [Fact]
    public async Task Gives_up_after_three_restarts_in_a_minute()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        for (var i = 0; i < 3; i++) await CrashAndWaitForRestartAsync(Feature.DnsRouting);
        var stopped = ObserveStopped();

        _services.Latest(Feature.DnsRouting).Crash();
        var args = await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(Feature.DnsRouting, args.Feature);
        Assert.False(args.ChangesLeftBehind);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.Empty(_journal.Entries);
        Assert.Contains("revert:dns:LunaDNS test", _configurator.Calls);
        Assert.Equal(4, _services.Starts(Feature.DnsRouting));
    }

    [Fact]
    public async Task Restart_budget_refills_after_a_minute()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        for (var i = 0; i < 3; i++) await CrashAndWaitForRestartAsync(Feature.DnsRouting);

        _time.Advance(TimeSpan.FromSeconds(61));
        await CrashAndWaitForRestartAsync(Feature.DnsRouting);

        Assert.True(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Failed_restarts_count_towards_the_limit()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _services.FailStart(Feature.DnsRouting, new DnsStubException(DnsStubError.PortInUse, "bận", new IOException()));
        var stopped = ObserveStopped();

        _services.Latest(Feature.DnsRouting).Crash();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Disable_does_not_trigger_a_restart()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        await _features.DisableAsync(Feature.DnsRouting);
        await Task.Delay(200);

        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }
}
