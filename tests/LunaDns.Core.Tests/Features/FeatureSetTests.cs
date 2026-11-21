using LunaDns.Core.Domains;
using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Proxy;
using LunaDns.Core.Tests.Journal;
using LunaDns.Core.Tests.Platform;
using LunaDns.Core.Tls;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureSetTests : IAsyncLifetime
{
    private const string PacUrl = "http://127.0.0.1:18080/proxy.pac";
    private static readonly string[] DnsEnableCalls = ["plan:dns", "apply:dns", "flush"];
    private static readonly string[] HostsEnableCalls = ["plan:hosts", "apply:hosts", "flush"];
    private static readonly Feature[] AllFeatures = [Feature.DnsRouting, Feature.SniProxy, Feature.PubgMode];
    private static readonly string[] DisableDnsCalls = ["revert:dns:LunaDNS test", "flush", "dispose"];
    private static readonly string[] LifoReverts =
    [
        "revert:hosts:127.0.0.1 prod-live-front.playbattlegrounds.com",
        "revert:proxy:" + PacUrl,
        "revert:dns:LunaDNS test",
    ];

    private readonly FakeConfigurator _configurator = new();
    private readonly FakeFeatureServices _services = new();
    private readonly ChangeJournal _journal;
    private readonly FeatureSet _features;

    public FeatureSetTests()
    {
        _journal = new ChangeJournal(new InMemoryJournalStore(), SystemChanges.Reverters(_configurator));
        _features = Create();
    }

    private FeatureSet Create(Func<FeatureTuning>? tuning = null) =>
        new(_journal, new SystemChanges(_journal, _configurator), _services, tuning);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _features.DisposeAsync();

    [Fact]
    public async Task Enable_dns_routing_starts_stub_then_records_and_applies_route()
    {
        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Enabled), result);
        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.Equal(FeatureDomains.Routed(new DomainSelection()), _services.LastDomains);
        Assert.Equal(FeatureKeys.DnsRouting, Assert.Single(_journal.Entries).Feature);
        Assert.Equal(DnsEnableCalls, _configurator.Calls);
    }

    [Fact]
    public async Task Enable_twice_is_unchanged()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(FeatureOutcome.Unchanged, result.Outcome);
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Stub_start_failure_writes_nothing()
    {
        _services.FailStart(Feature.DnsRouting, new DnsStubException(DnsStubError.PortInUse, "bận", new IOException()));

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.DnsPortBusy), result);
        Assert.Empty(_journal.Entries);
        Assert.Empty(_configurator.Calls);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Apply_failure_rolls_back_own_entries_and_stops_service()
    {
        _configurator.FailApply = true;

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.Other), result);
        Assert.Empty(_journal.Entries);
        Assert.Contains("revert:dns:LunaDNS test", _configurator.Calls);
        Assert.True(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Dns_route_conflict_maps_to_its_error()
    {
        _configurator.PlanDnsError = ConfiguratorError.DnsRouteConflict;

        var result = await _features.EnableAsync(Feature.DnsRouting);

        Assert.Equal(FeatureError.DnsRouteConflict, result.Error);
        Assert.Empty(_journal.Entries);
        Assert.True(_services.Latest(Feature.DnsRouting).Disposed);
    }

    [Fact]
    public async Task Existing_proxy_asks_for_override_then_enables_with_it()
    {
        _configurator.ExistingProxy = true;

        var first = await _features.EnableAsync(Feature.SniProxy);

        Assert.Equal(FeatureOutcome.NeedsProxyOverride, first.Outcome);
        Assert.Empty(_journal.Entries);
        Assert.True(_services.Latest(Feature.SniProxy).Disposed);
        Assert.False(_features.IsEnabled(Feature.SniProxy));

        var second = await _features.EnableAsync(Feature.SniProxy, allowProxyOverride: true);

        Assert.Equal(FeatureOutcome.Enabled, second.Outcome);
        var entry = Assert.Single(_journal.Entries);
        Assert.Equal(ChangeOps.AutoProxy, entry.Op);
        Assert.Equal(PacUrl, entry.Snapshot.GetProperty("NewValue").GetString());
    }

    [Fact]
    public async Task Proxy_port_busy_maps_to_its_error()
    {
        _services.FailStart(Feature.SniProxy, new SplitProxyException(ProxyStartError.PortInUse, "hết cổng", new IOException()));

        var result = await _features.EnableAsync(Feature.SniProxy);

        Assert.Equal(FeatureError.ProxyPortsBusy, result.Error);
    }

    [Fact]
    public async Task Pubg_port_busy_maps_to_its_error()
    {
        _services.FailStart(Feature.PubgMode, new SplitProxyException(ProxyStartError.AccessDenied, "443 bận", new IOException()));

        var result = await _features.EnableAsync(Feature.PubgMode);

        Assert.Equal(FeatureError.PubgPortBusy, result.Error);
    }

    [Fact]
    public async Task Tuning_comes_from_the_callback_at_start()
    {
        var tuning = new FeatureTuning(new HelloSplit(SplitMode.MidSni), TimeSpan.FromMilliseconds(5));
        await using var features = Create(() => tuning);

        await features.EnableAsync(Feature.SniProxy);

        Assert.Equal(tuning, _services.LastTuning);
    }

    [Fact]
    public async Task Pubg_mode_adds_pubg_hosts_and_starts_forwarder()
    {
        var result = await _features.EnableAsync(Feature.PubgMode);

        Assert.Equal(FeatureOutcome.Enabled, result.Outcome);
        Assert.Equal(FeatureDomains.Pubg, _services.LastDomains);
        Assert.Equal(HostsEnableCalls, _configurator.Calls);
        Assert.Equal(FeatureKeys.PubgMode, Assert.Single(_journal.Entries).Feature);
    }

    [Fact]
    public async Task Disable_reverts_first_then_stops_service()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _services.Latest(Feature.DnsRouting).OnDispose = () => _configurator.Calls.Add("dispose");

        var result = await _features.DisableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Disabled), result);
        Assert.Equal(DisableDnsCalls, _configurator.Calls.Skip(DnsEnableCalls.Length));
        Assert.Empty(_journal.Entries);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Disable_with_failed_revert_keeps_service_and_entry()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _configurator.FailRevert = true;

        var result = await _features.DisableAsync(Feature.DnsRouting);

        Assert.Equal(new FeatureResult(Feature.DnsRouting, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true), result);
        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.False(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Disable_when_off_is_unchanged()
    {
        var result = await _features.DisableAsync(Feature.SniProxy);

        Assert.Equal(FeatureOutcome.Unchanged, result.Outcome);
        Assert.Empty(_configurator.Calls);
    }

    [Fact]
    public async Task Disable_all_reverts_lifo_and_stops_everything()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        await _features.EnableAsync(Feature.SniProxy);
        await _features.EnableAsync(Feature.PubgMode);

        var result = await _features.DisableAllAsync();

        Assert.True(result.Success);
        Assert.Equal(LifoReverts, _configurator.Calls.Where(c => c.StartsWith("revert:", StringComparison.Ordinal)));
        Assert.All(AllFeatures, f => Assert.True(_services.Latest(f).Disposed));
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Disable_all_retries_entries_left_by_a_previous_run()
    {
        _journal.Record(FeatureKeys.DnsRouting, ChangeOps.DnsRoute, new DnsRouteSnapshot("LunaDNS old", [".steampowered.com"], "127.0.0.1", []));

        var result = await _features.DisableAllAsync();

        Assert.True(result.Success);
        Assert.Contains("revert:dns:LunaDNS old", _configurator.Calls);
        Assert.Empty(_journal.Entries);
    }

    [Fact]
    public async Task Shutdown_stops_services_even_when_revert_fails()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _configurator.FailRevert = true;

        var result = await _features.ShutdownAsync();

        Assert.False(result.Success);
        Assert.True(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.False(_features.IsEnabled(Feature.DnsRouting));
        Assert.Single(_journal.Entries);
    }

    [Fact]
    public async Task Status_reports_proxy_port_and_connections()
    {
        await _features.EnableAsync(Feature.SniProxy);
        ((FakeRunningProxy)_services.Latest(Feature.SniProxy)).ActiveConnections = 3;

        Assert.Equal(new FeatureStatus(false, true, false, FakeFeatureServices.DefaultProxyPort, 3), _features.GetStatus());
    }
}
