using LunaDns.Core.Features;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Features;

public sealed class RestartPolicyTests
{
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Allows_three_restarts_then_refuses()
    {
        var policy = new RestartPolicy(_time);

        Assert.True(policy.TryRecordRestart());
        Assert.True(policy.TryRecordRestart());
        Assert.True(policy.TryRecordRestart());
        Assert.False(policy.TryRecordRestart());
    }

    [Fact]
    public void Allows_again_after_the_window()
    {
        var policy = new RestartPolicy(_time);
        for (var i = 0; i < 3; i++) policy.TryRecordRestart();

        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.True(policy.TryRecordRestart());
    }

    [Fact]
    public void Window_slides_with_the_oldest_restart()
    {
        var policy = new RestartPolicy(_time);
        Assert.True(policy.TryRecordRestart());
        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.True(policy.TryRecordRestart());
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.True(policy.TryRecordRestart());
        Assert.False(policy.TryRecordRestart());

        _time.Advance(TimeSpan.FromSeconds(2));

        Assert.True(policy.TryRecordRestart());
    }
}
