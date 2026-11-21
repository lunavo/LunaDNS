using LunaDns.App.Features;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class AppServicesTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Leftover_journal_is_rolled_back_on_start()
    {
        _app.SeedJournal();

        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

        Assert.Equal(1, services.StartupRollback.Reverted);
        Assert.True(services.StartupRollback.Success);
        Assert.Empty(((LunaDns.App.Features.LocalFeatureController)services.Features).Journal.Entries);
        Assert.Equal(1, _app.Configurator.Reverts);
    }

    [Fact]
    public async Task Setting_settings_saves_file()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

        services.Settings = services.Settings with { CheckPubg = true, PubgWarningAccepted = true };

        var saved = new JsonFileSettingsStore(_app.Paths.SettingsFile).Load();
        Assert.True(saved.CheckPubg);
        Assert.True(saved.PubgWarningAccepted);
    }

    [Fact]
    public async Task Apply_domains_saves_settings_when_local()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        var selection = new DomainSelection { EnabledGroups = [DomainGroup.Reddit, DomainGroup.Custom], Custom = [new("example.org")] };

        var result = await services.ApplyDomainsAsync(selection);

        Assert.True(result.Success);
        Assert.True(services.Settings.Domains.SameAs(selection));
        Assert.True(new JsonFileSettingsStore(services.Paths.SettingsFile).Load().Domains.SameAs(selection));
        Assert.True((await services.GetDomainsAsync())!.SameAs(selection));
    }

    /// <summary>Brief dùng DnsRouting; ở đây dùng SniProxy (cổng cao) để test không cần cổng 53 trên máy chạy test.</summary>
    [Fact]
    public async Task Apply_domains_failure_keeps_old_settings()
    {
        await using var services = new AppServices(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);
        var enabled = await services.Features.EnableAsync(Feature.SniProxy, allowProxyOverride: true);
        Assert.Equal(FeatureOutcome.Enabled, enabled.Outcome);
        var before = services.Settings.Domains;

        var result = await services.ApplyDomainsAsync(new DomainSelection { EnabledGroups = [] });

        Assert.False(result.Success);
        Assert.Equal(ErrorMessages.For(FeatureError.NoDomains), result.Message);
        Assert.True(services.Settings.Domains.SameAs(before));
        Assert.True(new JsonFileSettingsStore(services.Paths.SettingsFile).Load().Domains.SameAs(before));
    }
}
