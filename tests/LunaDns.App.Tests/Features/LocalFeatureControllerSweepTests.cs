using LunaDns.App.Features;
using LunaDns.Core.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests.Features;

/// <summary>B5 + F7 bản Windows: mở app thì hoàn tác journal, rồi quét dấu vết LunaDNS mồ côi, trước khi tự bật lại.</summary>
public sealed class LocalFeatureControllerSweepTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private AppServices CreateServices() => new(_app.Paths, _app.Configurator, NullLoggerFactory.Instance);

    [Fact]
    public async Task Sweeps_once_after_startup_rollback()
    {
        _app.SeedJournal();

        await using var services = CreateServices();

        Assert.Equal(1, _app.Configurator.Sweeps);
        Assert.Equal(1, _app.Configurator.RevertsAtSweep);
    }

    [Fact]
    public async Task Sweeps_before_resume_turns_anything_back_on()
    {
        await using (var first = CreateServices())
        {
            await first.Features.EnableAsync(Feature.SniProxy);
            await first.ShutdownAsync(ShutdownReason.SessionEnd);
        }

        await using var second = CreateServices();
        Assert.Equal(2, _app.Configurator.Sweeps);
        Assert.False(second.Features.Status.SniProxy);
        await second.Features.ResumeAsync();

        Assert.True(second.Features.Status.SniProxy);
        Assert.Equal(2, _app.Configurator.Sweeps);
    }

    [Fact]
    public async Task Corrupt_journal_does_not_stop_the_app_and_is_quarantined()
    {
        File.WriteAllText(_app.Paths.JournalFile, "[{\"Id\": \"dở");

        await using var services = CreateServices();

        Assert.True(services.Features.StartupRollback.Success);
        Assert.Equal(1, _app.Configurator.Sweeps);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(_app.Paths.JournalFile)!, "journal.json.corrupt-*"));
    }

    [Fact]
    public async Task Failed_sweep_does_not_block_resume()
    {
        await using (var first = CreateServices())
        {
            await first.Features.EnableAsync(Feature.SniProxy);
            await first.ShutdownAsync(ShutdownReason.SessionEnd);
        }
        _app.Configurator.FailSweep = true;

        await using var second = CreateServices();
        await second.Features.ResumeAsync();

        Assert.True(second.Features.Status.SniProxy);
    }
}
