using LunaDns.Core.Dns;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Tests.Journal;
using LunaDns.Core.Tests.Platform;

namespace LunaDns.Core.Tests.Features;

public sealed class FeatureSetReapplyTests : IAsyncLifetime
{
    private static readonly string[] Steam = ["steampowered.com"];
    private static readonly string[] Reddit = ["reddit.com"];

    private readonly FakeConfigurator _configurator = new();
    private readonly FakeFeatureServices _services = new();
    private readonly ChangeJournal _journal;
    private readonly FeatureSet _features;

    public FeatureSetReapplyTests()
    {
        _journal = new ChangeJournal(new InMemoryJournalStore(), SystemChanges.Reverters(_configurator));
        _features = new FeatureSet(_journal, new SystemChanges(_journal, _configurator), _services, routedDomains: Steam);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _features.DisposeAsync();

    [Fact]
    public async Task Reapply_with_nothing_running_only_swaps_the_list()
    {
        var result = await _features.ReapplyAsync(Reddit);

        Assert.True(result.Success);
        Assert.Equal(Reddit, _features.RoutedDomains);
        Assert.Equal(0, _services.Starts(Feature.DnsRouting));
    }

    [Fact]
    public async Task Reapply_restarts_running_dns_and_proxy_with_new_list()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        await _features.EnableAsync(Feature.SniProxy);

        var result = await _features.ReapplyAsync(Reddit);

        Assert.True(result.Success);
        Assert.Equal(2, _services.Starts(Feature.DnsRouting));
        Assert.Equal(2, _services.Starts(Feature.SniProxy));
        Assert.Equal(Reddit, _services.LastDomains);
        Assert.True(_features.IsEnabled(Feature.DnsRouting));
        Assert.True(_features.IsEnabled(Feature.SniProxy));
        Assert.False(_services.Latest(Feature.DnsRouting).Disposed);
        Assert.True(_services.All(Feature.DnsRouting)[0].Disposed);
    }

    [Fact]
    public async Task Reapply_failure_restores_previous_list_and_features()
    {
        await _features.EnableAsync(Feature.DnsRouting);
        _services.FailStart(Feature.DnsRouting, new DnsStubException("giả lập cổng bận"), once: true);

        var result = await _features.ReapplyAsync(Reddit);

        Assert.False(result.Success);
        Assert.Equal(Steam, _features.RoutedDomains);
        Assert.True(result.PreviousRestored);
        Assert.True(_features.IsEnabled(Feature.DnsRouting));
    }

    [Fact]
    public async Task Reapply_with_empty_list_is_refused_while_a_feature_runs()
    {
        await _features.EnableAsync(Feature.DnsRouting);

        var result = await _features.ReapplyAsync([]);

        Assert.False(result.Success);
        Assert.Equal(FeatureError.NoDomains, result.Error);
        Assert.Equal(Steam, _features.RoutedDomains);
        Assert.Equal(1, _services.Starts(Feature.DnsRouting));
    }

    [Fact]
    public async Task Enable_with_empty_list_fails_with_no_domains()
    {
        await _features.ReapplyAsync([]);

        var result = await _features.EnableAsync(Feature.SniProxy);

        Assert.Equal(FeatureOutcome.Failed, result.Outcome);
        Assert.Equal(FeatureError.NoDomains, result.Error);
    }
}
