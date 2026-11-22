using System.Net;
using LunaDns.Platform.MacOS.Resolver;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class ResolverOrphanSweepTests : IDisposable
{
    private static readonly string[] Steam = ["steampowered.com", "steamcommunity.com"];
    private static readonly HashSet<string> None = [];
    private readonly TempDirectory _tmp = new();
    private readonly ResolverDnsRouter _router;

    public ResolverOrphanSweepTests()
    {
        _router = new ResolverDnsRouter(15353, _tmp.Combine("resolver"));
        Directory.CreateDirectory(_router.Directory);
    }

    public void Dispose() => _tmp.Dispose();

    private string FileFor(string host) => Path.Combine(_router.Directory, host);

    [Fact]
    public void Missing_directory_removes_nothing() =>
        Assert.Equal(0, new ResolverDnsRouter(15353, _tmp.Combine("none")).SweepOrphans(None));

    [Fact]
    public void Removes_lunadns_files_whose_marker_the_journal_does_not_hold()
    {
        _router.Apply(_router.Plan(Steam, IPAddress.Loopback));

        Assert.Equal(2, _router.SweepOrphans(None));
        Assert.Empty(_router.ListOurs());
    }

    [Fact]
    public void Keeps_files_whose_marker_the_journal_still_holds()
    {
        var kept = _router.Plan(["steampowered.com"], IPAddress.Loopback);
        _router.Apply(kept);
        _router.Apply(_router.Plan(["steamcommunity.com"], IPAddress.Loopback));
        HashSet<string> covered = [kept.Marker];

        Assert.Equal(1, _router.SweepOrphans(covered));
        Assert.Equal(["steampowered.com"], _router.ListOurs());
    }

    [Theory]
    [InlineData("nameserver 10.0.0.1\n")]                       // không dấu: script tay
    [InlineData("# Surfshark\nnameserver 10.0.0.1\n")]          // dấu phần mềm khác
    [InlineData("# LunaDNS \nnameserver 127.0.0.1\n")]          // dấu thiếu id
    [InlineData("# LunaDNSabc\nnameserver 127.0.0.1\n")]
    [InlineData("nameserver 127.0.0.1\n# LunaDNS abc\n")]       // dấu không ở dòng đầu
    public void Never_touches_foreign_files(string content)
    {
        File.WriteAllText(FileFor("example.com"), content);

        Assert.Equal(0, _router.SweepOrphans(None));
        Assert.Equal(content, File.ReadAllText(FileFor("example.com")));
    }

    [Fact]
    public void Leaves_symlinks_alone()
    {
        File.WriteAllText(_tmp.Combine("target"), "# LunaDNS abc\nnameserver 127.0.0.1\n");
        File.CreateSymbolicLink(FileFor("example.com"), _tmp.Combine("target"));

        Assert.Equal(0, _router.SweepOrphans(None));
        Assert.True(File.Exists(FileFor("example.com")));
    }
}
