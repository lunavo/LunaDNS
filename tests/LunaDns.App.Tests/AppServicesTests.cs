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
}
