namespace LunaDns.App.Tests;

/// <summary>Mutex có thread affinity: mọi test ở đây chạy đồng bộ trên một luồng.</summary>
public sealed class SingleInstanceTests
{
    private readonly string _name = $@"Global\LunaDNS.Test.{Guid.NewGuid():N}";

    [WindowsFact]
    public void Second_acquire_fails_while_held()
    {
        using var first = SingleInstance.TryAcquire(_name);

        Assert.NotNull(first);
        Assert.Null(SingleInstance.TryAcquire(_name));
    }

    [WindowsFact]
    public void Can_acquire_again_after_dispose()
    {
        SingleInstance.TryAcquire(_name)!.Dispose();

        using var again = SingleInstance.TryAcquire(_name);

        Assert.NotNull(again);
    }

    [WindowsFact]
    public void Signal_show_reaches_listener()
    {
        using var owner = SingleInstance.TryAcquire(_name)!;
        using var shown = new ManualResetEventSlim();
        owner.ListenForShow(shown.Set);

        Assert.True(SingleInstance.SignalShow(_name));
        Assert.True(shown.Wait(TimeSpan.FromSeconds(5)));
    }

    [WindowsFact]
    public void Signal_show_without_owner_returns_false() =>
        Assert.False(SingleInstance.SignalShow(_name));
}
