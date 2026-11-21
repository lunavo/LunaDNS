using LunaDns.Core.Domains;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests.Features;

public sealed class LocalFeatureControllerDomainListTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private AppServices CreateServices() => new(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

    [Fact]
    public async Task Unsigned_list_is_refused_and_nothing_is_written()
    {
        await using var services = CreateServices();
        var result = await services.OfferDomainListAsync("""{"keyId":"x","payload":"e30=","sig":"AA=="}"""u8.ToArray());
        Assert.False(result.Success);
        Assert.False(File.Exists(services.Paths.DomainListFile));
    }

    [Fact]
    public async Task Switch_is_saved_to_settings()
    {
        await using var services = CreateServices();
        Assert.True((await services.SetDomainListEnabledAsync(false)).Success);
        Assert.False(services.Settings.DomainListUpdates);
        Assert.False((await services.GetDomainListAsync())!.Enabled);
        Assert.False(new LunaDns.Core.Settings.JsonFileSettingsStore(services.Paths.SettingsFile).Load().DomainListUpdates);
    }

    [Fact]
    public async Task Catalog_without_list_is_builtin()
    {
        await using var services = CreateServices();
        Assert.Same(EffectiveCatalog.Builtin, await services.CatalogAsync());
    }
}
