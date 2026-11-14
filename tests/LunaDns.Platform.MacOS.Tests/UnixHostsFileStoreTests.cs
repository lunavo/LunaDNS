using LunaDns.Platform.MacOS.Hosts;

namespace LunaDns.Platform.MacOS.Tests;

public sealed class UnixHostsFileStoreTests : IDisposable
{
    private readonly TempDirectory _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void Writes_through_a_symlinked_directory_and_keeps_mode()
    {
        var real = Directory.CreateDirectory(_tmp.Combine("private-etc")).FullName;
        var file = Path.Combine(real, "hosts");
        File.WriteAllText(file, "127.0.0.1 localhost\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        var link = _tmp.Combine("etc");
        Directory.CreateSymbolicLink(link, real);

        var store = new UnixHostsFileStore(Path.Combine(link, "hosts"));
        store.Write("127.0.0.1 localhost\n127.0.0.1 pubg.com # LunaDNS\n"u8.ToArray());

        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
        Assert.Contains("pubg.com", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(file));
        Assert.Equal(File.ReadAllBytes(file), store.Read());
    }
}
