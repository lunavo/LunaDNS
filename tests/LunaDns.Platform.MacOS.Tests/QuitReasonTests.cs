using LunaDns.Platform.MacOS.AppleEvents;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class QuitReasonTests
{
    [Fact]
    public void Four_char_codes_match_apple_event_constants()
    {
        Assert.Equal(0x7768793Fu, QuitReason.FourCc("why?"));
        Assert.Equal(0x72657374u, QuitReason.FourCc("rest"));
        Assert.Throws<ArgumentException>(() => QuitReason.FourCc("why"));
    }

    [Theory]
    [InlineData("logo", true)]
    [InlineData("rlgo", true)]
    [InlineData("rrst", true)]
    [InlineData("rest", true)]
    [InlineData("rsdn", true)]
    [InlineData("shut", true)]
    [InlineData("quit", false)]
    public void Only_logout_restart_and_shutdown_end_the_session(string code, bool sessionEnd) =>
        Assert.Equal(sessionEnd, QuitReason.IsSessionEndReason(QuitReason.FourCc(code)));

    [Fact]
    public void Outside_an_apple_event_it_is_a_user_quit()
    {
        Assert.Equal(0u, QuitReason.CurrentReason());
        Assert.False(QuitReason.IsSessionEnding());
        Assert.False(QuitReason.IsSessionEndReason(0));
    }
}
