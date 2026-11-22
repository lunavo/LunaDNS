using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class CleanupCommandTests : IDisposable
{
    private readonly TestApp _app = new();

    public void Dispose() => _app.Dispose();

    private int Run() => CleanupCommand.Run(_app.Paths, () => _app.Configurator, NullLogger.Instance, _app.MutexName);

    [WindowsFact]
    public void Does_nothing_while_app_is_running()
    {
        _app.SeedJournal();
        using var running = SingleInstance.TryAcquire(_app.MutexName);

        Assert.Equal(0, Run());
        Assert.Equal(0, _app.Configurator.Reverts);
        Assert.Equal(1, _app.JournalCount());
    }

    [WindowsFact]
    public void Missing_journal_is_success_and_creates_nothing()
    {
        Assert.Equal(0, Run());
        Assert.False(File.Exists(_app.Paths.JournalFile));
    }

    [WindowsFact]
    public void Rolls_back_leftovers()
    {
        _app.SeedJournal();

        Assert.Equal(0, Run());
        Assert.Equal(1, _app.Configurator.Reverts);
        Assert.Equal(0, _app.JournalCount());
    }

    [WindowsFact]
    public void Failed_revert_returns_one_and_keeps_entry()
    {
        _app.SeedJournal();
        _app.Configurator.FailRevert = true;

        Assert.Equal(1, Run());
        Assert.Equal(1, _app.JournalCount());
    }

    [WindowsFact]
    public void Sweeps_orphans_even_without_a_journal()
    {
        Assert.Equal(0, Run());
        Assert.Equal(1, _app.Configurator.Sweeps);
    }

    [WindowsFact]
    public void Sweeps_after_rollback()
    {
        _app.SeedJournal();

        Assert.Equal(0, Run());
        Assert.Equal(1, _app.Configurator.RevertsAtSweep);
    }

    [WindowsFact]
    public void Corrupt_journal_is_quarantined_and_swept_not_a_crash()
    {
        File.WriteAllText(_app.Paths.JournalFile, "[{\"Id\": \"dở");

        Assert.Equal(0, Run());
        Assert.Equal(1, _app.Configurator.Sweeps);
        Assert.False(File.Exists(_app.Paths.JournalFile));
    }

    [WindowsFact]
    public void Failed_sweep_returns_one()
    {
        _app.Configurator.FailSweep = true;

        Assert.Equal(1, Run());
    }

    [WindowsFact]
    public void Does_not_sweep_while_app_is_running()
    {
        using var running = SingleInstance.TryAcquire(_app.MutexName);

        Assert.Equal(0, Run());
        Assert.Equal(0, _app.Configurator.Sweeps);
    }
}
