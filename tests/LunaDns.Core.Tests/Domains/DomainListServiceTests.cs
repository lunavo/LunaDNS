using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using Microsoft.Extensions.Time.Testing;
using static LunaDns.Core.Tests.Domains.TestDomainListKey;

namespace LunaDns.Core.Tests.Domains;

public sealed class DomainListServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-domainlist-svc-").FullName;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 22, 7, 0, 0, TimeSpan.Zero));
    private readonly List<IReadOnlyList<string>> _reapplied = [];
    private DomainSelection _selection = new();
    private ReapplyResult _next = new(true, false);

    private string FilePath => Path.Combine(_dir, "domain-list.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private DomainListService Create(bool enabled = true) => new(new DomainListStore(FilePath, Keys), Keys, enabled, () => _selection,
        (domains, _) => { _reapplied.Add(domains); return Task.FromResult(_next); }, _time);

    private static byte[] Steam(long serial, params string[] domains) => Signed(serial, new() { ["steam"] = [.. domains] });

    [Fact]
    public async Task Accepted_list_reapplies_then_saves()
    {
        var service = Create();

        var result = await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Equal(1, result.Added);
        Assert.Contains("cdn.example.net", Assert.Single(_reapplied));
        Assert.Contains("cdn.example.net", service.Catalog.For(DomainGroup.Steam));
        Assert.Equal(5, new DomainListStore(FilePath, Keys).Load().HighestSerial);
        Assert.Equal(_time.GetUtcNow(), service.State.CheckedAt);
    }

    [Fact]
    public async Task Failed_reapply_keeps_old_list_and_writes_nothing()
    {
        var service = Create();
        _next = new ReapplyResult(false, true, FeatureError.ListenerFailed);

        var result = await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal(DomainListOutcome.ReapplyFailed, result.Outcome);
        Assert.Equal(FeatureError.ListenerFailed, result.Error);
        Assert.Empty(service.Catalog.Added(DomainGroup.Steam));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public async Task List_for_disabled_group_saves_without_reapply()
    {
        var service = Create();

        var result = await service.OfferAsync(Signed(5, new() { ["reddit"] = ["redditblog.com"] }));

        Assert.Equal(DomainListOutcome.Accepted, result.Outcome);
        Assert.Empty(_reapplied);
        Assert.Equal(5, service.State.Serial);
    }

    [Fact]
    public async Task Same_serial_only_updates_check_time()
    {
        var service = Create();
        await service.OfferAsync(Steam(5, "cdn.example.net"));
        _reapplied.Clear();
        _time.Advance(TimeSpan.FromHours(25));

        var result = await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal(DomainListOutcome.NotNewer, result.Outcome);
        Assert.Empty(_reapplied);
        Assert.Equal(_time.GetUtcNow(), new DomainListStore(FilePath, Keys).Load().CheckedAt);
    }

    [Fact]
    public async Task Disabled_service_refuses_offers()
    {
        var service = Create(enabled: false);
        Assert.Equal(DomainListOutcome.Disabled, (await service.OfferAsync(Steam(5, "cdn.example.net"))).Outcome);
    }

    [Fact]
    public async Task Switching_off_drops_layer_two_and_on_restores_without_download()
    {
        var service = Create();
        await service.OfferAsync(Steam(5, "cdn.example.net"));
        _reapplied.Clear();

        Assert.Equal(DomainListOutcome.Accepted, (await service.SetEnabledAsync(false)).Outcome);
        Assert.DoesNotContain("cdn.example.net", Assert.Single(_reapplied));
        Assert.Empty(service.Catalog.Added(DomainGroup.Steam));
        Assert.False(service.State.Enabled);

        await service.SetEnabledAsync(true);
        Assert.Contains("cdn.example.net", service.Catalog.For(DomainGroup.Steam));
    }

    [Fact]
    public async Task Switch_failure_rolls_back_the_flag()
    {
        var service = Create();
        await service.OfferAsync(Steam(5, "cdn.example.net"));
        _next = new ReapplyResult(false, true, FeatureError.ListenerFailed);

        Assert.Equal(DomainListOutcome.ReapplyFailed, (await service.SetEnabledAsync(false)).Outcome);
        Assert.True(service.State.Enabled);
    }

    [Fact]
    public async Task Turning_back_on_does_not_accept_older_serial()
    {
        var service = Create();
        await service.OfferAsync(Steam(9, "cdn.example.net"));
        await service.SetEnabledAsync(false);
        await service.SetEnabledAsync(true);

        Assert.Equal(DomainListOutcome.Downgrade, (await service.OfferAsync(Steam(8, "old.example.net"))).Outcome);
    }

    [Fact]
    public async Task Custom_entries_are_never_touched()
    {
        _selection = new DomainSelection { Custom = [new CustomDomain("cdn.example.net")] };
        var service = Create();

        await service.OfferAsync(Steam(5, "cdn.example.net"));

        Assert.Equal([new CustomDomain("cdn.example.net")], _selection.Custom);
    }

    [Fact]
    public async Task Service_loads_stored_list_on_start()
    {
        await Create().OfferAsync(Steam(5, "cdn.example.net"));
        Assert.Contains("cdn.example.net", Create().Catalog.For(DomainGroup.Steam));
    }

    [Fact]
    public async Task With_catalog_runs_inside_the_gate()
    {
        var service = Create();
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var held = service.WithCatalogAsync(async _ => { inside.SetResult(); await release.Task; return 0; });
        await inside.Task;

        var offer = service.OfferAsync(Steam(5, "cdn.example.net"));
        Assert.False(offer.IsCompleted);

        release.SetResult();
        await held;
        Assert.Equal(DomainListOutcome.Accepted, (await offer).Outcome);
    }
}
