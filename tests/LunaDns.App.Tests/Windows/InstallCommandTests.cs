using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.App.Tests;

public sealed class InstallCommandTests : IDisposable
{
    private const string ProgramFiles = @"C:\Program Files";
    private const string InstalledExe = @"C:\Program Files\LunaDNS\LunaDNS.exe";
    private readonly TestApp _app = new();
    private readonly FakeDataDirectoryAcl _acl = new();
    private readonly FakeCleanupTaskStore _tasks = new();

    public void Dispose() => _app.Dispose();

    private int Run(string exe) => InstallCommand.Run(_app.Paths, exe, ProgramFiles, _acl, _tasks, NullLogger.Instance);

    [Fact]
    public void Refuses_outside_program_files_and_changes_nothing()
    {
        Assert.Equal(InstallCommand.WrongLocation, Run(@"C:\Users\a\Downloads\LunaDNS\LunaDNS.exe"));
        Assert.Empty(_acl.Applied);
        Assert.Empty(_tasks.Calls);
    }

    [WindowsFact]
    public void Applies_acl_before_registering_task()
    {
        Assert.Equal(0, Run(InstalledExe));
        Assert.Equal([_app.Paths.DataDirectory], _acl.Applied);
        Assert.Equal(InstalledExe, _tasks.RegisteredExe);
    }

    [WindowsFact]
    public void Acl_failure_returns_1_without_task()
    {
        _acl.ApplyError = new UnauthorizedAccessException();

        Assert.Equal(1, Run(InstalledExe));
        Assert.Empty(_tasks.Calls);
    }

    [WindowsFact]
    public void Task_failure_returns_1()
    {
        _tasks.Error = new InvalidOperationException("schtasks");

        Assert.Equal(1, Run(InstalledExe));
    }
}
