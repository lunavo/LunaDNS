using LunaDns.App.MacOS;

namespace LunaDns.App.Tests.MacOS;

public sealed class DaemonSetupTests
{
    [Fact]
    public async Task Path_is_passed_as_argv_not_spliced_into_script()
    {
        string? file = null;
        IReadOnlyList<string>? args = null;
        var setup = new DaemonSetup((f, a, _) =>
        {
            (file, args) = (f, a);
            return Task.FromResult((0, "Đã cài"));
        });
        const string cli = "/Applications/Luna \"DNS\".app/Contents/MacOS/daemon/lunadns";

        var result = await setup.InstallAsync(cli);

        Assert.True(result.Success);
        Assert.Equal(DaemonSetup.Osascript, file);
        Assert.Equal(cli, args![^1]);
        Assert.DoesNotContain(args.Take(args.Count - 1), a => a.Contains("Luna", StringComparison.Ordinal));
        Assert.Contains(args, a => a.Contains("with administrator privileges", StringComparison.Ordinal));
    }

    [Fact]
    public async Task User_cancel_is_reported_as_cancelled()
    {
        var setup = new DaemonSetup((_, _, _) => Task.FromResult((1, "execution error: User canceled. (-128)")));

        var result = await setup.InstallAsync("/x/lunadns");

        Assert.False(result.Success);
        Assert.True(result.Cancelled);
    }

    [Fact]
    public void Cli_is_found_only_in_daemon_folder()
    {
        using var dir = new TempDirectory();
        Assert.Null(DaemonSetup.FindCli(dir.Path));

        Directory.CreateDirectory(dir.PathOf("daemon"));
        File.WriteAllText(dir.PathOf("daemon/lunadns"), "");

        Assert.Equal(dir.PathOf("daemon/lunadns"), DaemonSetup.FindCli(dir.Path));
    }
}
