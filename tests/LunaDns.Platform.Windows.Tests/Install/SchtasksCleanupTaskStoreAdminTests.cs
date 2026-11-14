using LunaDns.Platform.Windows.Install;

namespace LunaDns.Platform.Windows.Tests.Install;

/// <summary>Đăng ký task thật dưới tên riêng của test để chắc schtasks nhận XML. Không bao giờ dùng \LunaDNS\Cleanup.</summary>
public sealed class SchtasksCleanupTaskStoreAdminTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly SchtasksCleanupTaskStore _store;

    public SchtasksCleanupTaskStoreAdminTests()
    {
        _store = new SchtasksCleanupTaskStore(_temp.Path, $@"\LunaDNS.Tests\Cleanup-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        _store.Delete();
        _temp.Dispose();
    }

    [AdminFact]
    public void Register_then_delete_round_trips()
    {
        Assert.False(_store.Exists());

        _store.Register(Path.Combine(Environment.SystemDirectory, "whoami.exe"));

        Assert.True(_store.Exists());
        Assert.Empty(Directory.EnumerateFiles(_temp.Path));
        _store.Delete();
        Assert.False(_store.Exists());
    }

    [AdminFact]
    public void Register_twice_replaces()
    {
        var exe = Path.Combine(Environment.SystemDirectory, "whoami.exe");
        _store.Register(exe);
        _store.Register(exe);

        Assert.True(_store.Exists());
    }

    [AdminFact]
    public void Delete_missing_task_is_noop()
    {
        _store.Delete();
        Assert.False(_store.Exists());
    }
}
