using LunaDns.App.MacOS;

namespace LunaDns.App.Tests.MacOS;

/// <summary>Unix socket giới hạn ~104 byte đường dẫn: dùng thư mục ngắn dưới /tmp thay vì $TMPDIR.</summary>
public sealed class MacAppInstanceTests : IDisposable
{
    private readonly string _dir = Path.Combine("/tmp", "lunadns-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Second_acquire_fails_while_held()
    {
        using var first = MacAppInstance.TryAcquire(_dir);

        Assert.NotNull(first);
        Assert.Null(MacAppInstance.TryAcquire(_dir));
    }

    [Fact]
    public void Can_acquire_again_after_dispose()
    {
        MacAppInstance.TryAcquire(_dir)!.Dispose();

        using var again = MacAppInstance.TryAcquire(_dir);

        Assert.NotNull(again);
    }

    [Fact]
    public void Signal_show_reaches_listener()
    {
        using var owner = MacAppInstance.TryAcquire(_dir)!;
        using var shown = new ManualResetEventSlim();
        owner.ListenForShow(shown.Set);

        Assert.True(MacAppInstance.SignalShow(_dir));
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Signal_show_without_owner_returns_false()
    {
        Directory.CreateDirectory(_dir);

        Assert.False(MacAppInstance.SignalShow(_dir));
    }

    [Fact]
    public void Stale_socket_from_crashed_run_is_replaced()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(MacAppInstance.SocketPath(_dir), "cũ");

        using var owner = MacAppInstance.TryAcquire(_dir);

        Assert.NotNull(owner);
    }
}
