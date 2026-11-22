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
}
