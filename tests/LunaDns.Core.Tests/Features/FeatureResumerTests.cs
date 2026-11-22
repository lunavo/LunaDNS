using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Settings;
using LunaDns.Core.Tests.Journal;
using LunaDns.Core.Tests.Platform;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureResumerTests : IAsyncLifetime
{
    private static readonly TimeSpan[] NoDelays = [TimeSpan.Zero, TimeSpan.Zero];

    private readonly FakeConfigurator _configurator = new();
    private readonly FakeFeatureServices _services = new();
    private readonly ChangeJournal _journal;
    private readonly FeatureSet _features;
    private AppSettings _settings = new();
    private int _saves;

    public FeatureResumerTests()
    {
        _journal = new ChangeJournal(new InMemoryJournalStore(), SystemChanges.Reverters(_configurator));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, _configurator), _services);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _features.DisposeAsync();

    private FeatureResumer Create(IReadOnlyList<TimeSpan>? delays = null, Action<AppSettings>? save = null) =>
        new(_features, () => _settings, save ?? (s =>
        {
            _saves++;
            _settings = s;
        }), delays ?? NoDelays);

    [Fact]
    public void Wanted_features_helpers()
    {
        var both = new WantedFeatures(true, true);
        Assert.True(WantedFeatures.None.IsEmpty());
        Assert.Equal([Feature.DnsRouting, Feature.SniProxy], both.List());
        Assert.Equal(new WantedFeatures(false, true), both.Missing(new FeatureStatus(true, false, false, null, 0)));
        Assert.Equal(new WantedFeatures(true, false), WantedFeatures.None.With(Feature.DnsRouting, true));
        Assert.Equal(WantedFeatures.None, WantedFeatures.None.With(Feature.PubgMode, true));
        Assert.Equal(both, new WantedFeatures(true, false).Union(new WantedFeatures(false, true)));
    }

    [Fact]
    public async Task Resumes_wanted_features_dns_first_without_touching_settings()
    {
        _settings = _settings with { Wanted = new WantedFeatures(true, true) };
        var resumer = Create();

        await resumer.RunAsync(rollbackClean: true);

        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.True(_features.IsEnabled(Feature.SniProxy));
        Assert.False(_features.IsEnabled(Feature.PubgMode));
        Assert.Equal(new ResumeState(true, new WantedFeatures(true, true), false, null), resumer.State);
        Assert.Equal(0, _saves);
    }

    [Fact]
    public async Task Switch_off_or_nothing_wanted_enables_nothing()
    {
        _settings = _settings with { AutoResume = false, Wanted = new WantedFeatures(true, false) };
        await Create().RunAsync(rollbackClean: true);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));

        _settings = new AppSettings();
        await Create().RunAsync(rollbackClean: true);
        Assert.Equal(0, _services.Starts(Feature.DnsRouting));
    }

    [Fact]
    public async Task Dirty_startup_rollback_blocks_resume_and_reports_it()
    {
        _settings = _settings with { Wanted = new WantedFeatures(false, true) };
        var resumer = Create();

        await resumer.RunAsync(rollbackClean: false);

        Assert.Equal(0, _services.Starts(Feature.SniProxy));
        Assert.Equal(new FeatureResult(Feature.SniProxy, FeatureOutcome.Failed, FeatureError.RollbackFailed), resumer.State.Failure);
    }

    [Fact]
    public async Task Never_overrides_an_existing_proxy_and_does_not_retry_that()
    {
        _configurator.ExistingProxy = true;
        _settings = _settings with { Wanted = new WantedFeatures(true, true) };
        var resumer = Create();

        await resumer.RunAsync(rollbackClean: true);

        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.False(_features.IsEnabled(Feature.SniProxy));
        Assert.Equal(1, _services.Starts(Feature.SniProxy));
        Assert.Equal(FeatureOutcome.NeedsProxyOverride, resumer.State.Failure!.Outcome);
    }

    [Fact]
    public async Task Failed_enable_is_retried_until_it_works()
    {
        _services.FailStart(Feature.DnsRouting, new DnsStubException(DnsStubError.PortInUse, "chưa sẵn sàng", new IOException()), once: true);
        _settings = _settings with { Wanted = new WantedFeatures(true, false) };
        var resumer = Create();

        await resumer.RunAsync(rollbackClean: true);

        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.Equal(1, _services.Starts(Feature.DnsRouting)); // Starts chỉ đếm lần mở thành công
        Assert.Null(resumer.State.Failure);
    }

    [Fact]
    public async Task Retries_stop_after_the_last_delay_and_keep_the_failure()
    {
        _services.FailStart(Feature.DnsRouting, new DnsStubException(DnsStubError.PortInUse, "bận", new IOException()));
        _settings = _settings with { Wanted = new WantedFeatures(true, false) };
        var resumer = Create();

        await resumer.RunAsync(rollbackClean: true);

        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.Equal(FeatureOutcome.Failed, resumer.State.Failure!.Outcome);
        Assert.False(resumer.State.Restoring);
    }

    [Fact]
    public async Task Explicit_command_stops_the_retry_loop()
    {
        _services.FailStart(Feature.DnsRouting, new DnsStubException(DnsStubError.PortInUse, "bận", new IOException()));
        _settings = _settings with { Wanted = new WantedFeatures(true, false) };
        var resumer = Create([TimeSpan.FromMinutes(5)]);

        var run = resumer.RunAsync(rollbackClean: true);
        Assert.True(resumer.State.Restoring);
        resumer.Forget();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.False(resumer.State.Restoring);
        Assert.Null(resumer.State.Failure);
        Assert.Equal(WantedFeatures.None, _settings.Wanted);
    }

    [Fact]
    public void Record_and_forget_update_wanted_and_skip_pubg()
    {
        var resumer = Create();

        resumer.Record(Feature.DnsRouting, true);
        resumer.Record(Feature.SniProxy, true);
        resumer.Record(Feature.PubgMode, true);
        Assert.Equal(new WantedFeatures(true, true), _settings.Wanted);

        resumer.Record(Feature.SniProxy, false);
        Assert.Equal(new WantedFeatures(true, false), _settings.Wanted);
        var saves = _saves;
        resumer.Record(Feature.DnsRouting, true);
        Assert.Equal(saves, _saves); // không đổi thì không ghi file

        resumer.Forget();
        Assert.Equal(WantedFeatures.None, _settings.Wanted);
    }

    [Fact]
    public async Task Record_clears_the_failure_of_that_feature_only()
    {
        _configurator.ExistingProxy = true;
        _settings = _settings with { Wanted = new WantedFeatures(false, true) };
        var resumer = Create();
        await resumer.RunAsync(rollbackClean: true);

        resumer.Record(Feature.DnsRouting, true);
        Assert.NotNull(resumer.State.Failure);
        resumer.Record(Feature.SniProxy, true);
        Assert.Null(resumer.State.Failure);
    }

    [Fact]
    public void Save_failure_is_swallowed()
    {
        var resumer = Create(save: _ => throw new IOException("đĩa đầy"));

        resumer.Record(Feature.DnsRouting, true);

        Assert.False(resumer.SetEnabled(false));
        Assert.True(_settings.AutoResume);
    }

    [Fact]
    public void Set_enabled_saves_the_switch()
    {
        var resumer = Create();

        Assert.True(resumer.SetEnabled(false));

        Assert.False(_settings.AutoResume);
        Assert.False(resumer.State.Enabled);
    }
}
