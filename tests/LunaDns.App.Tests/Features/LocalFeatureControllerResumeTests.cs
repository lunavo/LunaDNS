using LunaDns.App.Features;
using LunaDns.Core.Features;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests.Features;

/// <summary>
/// F1 bản Windows: wanted nằm trong settings.json của app, tự bật lúc mở app. Dùng Proxy SNI (cổng cao) để test không cần cổng 53.
/// </summary>
public sealed class LocalFeatureControllerResumeTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private AppServices CreateServices() => new(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

    private WantedFeatures SavedWanted() => new JsonFileSettingsStore(_app.Paths.SettingsFile).Load().Wanted!;

    [Fact]
    public async Task Explicit_commands_record_wanted_in_app_settings()
    {
        await using var services = CreateServices();

        await services.Features.EnableAsync(Feature.SniProxy);
        Assert.Equal(new WantedFeatures(false, true), SavedWanted());
        Assert.Equal(new WantedFeatures(false, true), services.Features.Resume!.Wanted);

        await services.Features.DisableAllAsync();
        Assert.Equal(WantedFeatures.None, SavedWanted());
    }

    [Fact]
    public async Task Session_end_keeps_wanted_and_next_start_turns_it_back_on()
    {
        await using (var first = CreateServices())
        {
            await first.Features.EnableAsync(Feature.SniProxy);
            await first.ShutdownAsync(ShutdownReason.SessionEnd);
            Assert.False(first.Features.Status.SniProxy);
        }

        await using var second = CreateServices();
        Assert.False(second.Features.Status.SniProxy);
        await second.Features.ResumeAsync();

        Assert.True(second.Features.Status.SniProxy);
        Assert.Equal(new WantedFeatures(false, true), SavedWanted());
    }

    [Fact]
    public async Task User_quit_forgets_so_next_start_enables_nothing()
    {
        await using (var first = CreateServices())
        {
            await first.Features.EnableAsync(Feature.SniProxy);
            await first.ShutdownAsync(ShutdownReason.UserQuit);
        }

        await using var second = CreateServices();
        await second.Features.ResumeAsync();

        Assert.False(second.Features.Status.SniProxy);
        Assert.Equal(WantedFeatures.None, SavedWanted());
    }

    [Fact]
    public async Task Switch_off_is_saved_and_blocks_resume()
    {
        await using (var first = CreateServices())
        {
            await first.Features.EnableAsync(Feature.SniProxy);
            Assert.Null(await first.Features.SetAutoResumeAsync(false));
            await first.ShutdownAsync(ShutdownReason.SessionEnd);
        }

        await using var second = CreateServices();
        await second.Features.ResumeAsync();

        Assert.False(second.Features.Status.SniProxy);
        Assert.Equal(new ResumeState(false, new WantedFeatures(false, true), false, null), second.Features.Resume);
    }
}
