using LunaDns.Platform.Windows.Install;

namespace LunaDns.App.Tests;

internal sealed class FakeDataDirectoryAcl : IDataDirectoryAcl
{
    public List<string> Applied { get; } = [];

    public Exception? ApplyError { get; set; }

    public void Apply(string directory)
    {
        if (ApplyError is not null) throw ApplyError;
        Applied.Add(directory);
    }

    public bool IsCompliant(string directory) => Applied.Contains(directory);
}

internal sealed class FakeCleanupTaskStore : ICleanupTaskStore
{
    public string? RegisteredExe { get; private set; }

    public Exception? Error { get; set; }

    public List<string> Calls { get; } = [];

    public bool Exists() => RegisteredExe is not null;

    public void Register(string exePath)
    {
        Calls.Add("Register");
        if (Error is not null) throw Error;
        RegisteredExe = exePath;
    }

    public void Delete()
    {
        Calls.Add("Delete");
        if (Error is not null) throw Error;
        RegisteredExe = null;
    }
}
