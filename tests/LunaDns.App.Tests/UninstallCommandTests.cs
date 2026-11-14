using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class UninstallCommandTests : IDisposable
{
    private readonly TestApp _app = new();
    private readonly FakeCleanupTaskStore _tasks = new();

    public void Dispose() => _app.Dispose();

    private int Run() => UninstallCommand.Run(_app.Paths, () => _app.Configurator, _tasks, NullLogger.Instance, _app.MutexName);

    [Fact]
    public void Refuses_while_app_is_running()
    {
        _app.SeedJournal();
        using var running = SingleInstance.TryAcquire(_app.MutexName);

        Assert.Equal(UninstallCommand.AppRunning, Run());
        Assert.Equal(1, _app.JournalCount());
        Assert.Empty(_tasks.Calls);
    }

    [Fact]
    public void Reverts_journal_then_deletes_task()
    {
        _app.SeedJournal();
        _tasks.Register(@"C:\Program Files\LunaDNS\LunaDNS.exe");

        Assert.Equal(0, Run());
        Assert.Equal(1, _app.Configurator.Reverts);
        Assert.Equal(0, _app.JournalCount());
        Assert.False(_tasks.Exists());
    }

    [Fact]
    public void Failed_revert_keeps_journal_and_task()
    {
        _app.SeedJournal();
        _tasks.Register(@"C:\Program Files\LunaDNS\LunaDNS.exe");
        _app.Configurator.FailRevert = true;

        Assert.Equal(1, Run());
        Assert.Equal(1, _app.JournalCount());
        Assert.True(_tasks.Exists());
    }

    [Fact]
    public void Task_delete_failure_returns_1()
    {
        _tasks.Error = new InvalidOperationException("schtasks");

        Assert.Equal(1, Run());
    }

    [Fact]
    public void Without_journal_or_task_succeeds()
    {
        Assert.Equal(0, Run());
    }

    [Fact]
    public void TryDeleteData_removes_data_directory()
    {
        _app.SeedJournal();

        Assert.True(UninstallCommand.TryDeleteData(_app.Paths));
        Assert.False(Directory.Exists(_app.Paths.DataDirectory));
        Assert.True(UninstallCommand.TryDeleteData(_app.Paths));
    }
}
