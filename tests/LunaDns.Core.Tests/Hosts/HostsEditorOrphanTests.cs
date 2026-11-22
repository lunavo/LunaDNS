using System.Text;
using LunaDns.Core.Hosts;

namespace LunaDns.Core.Tests.Hosts;

public class HostsEditorOrphanTests
{
    private static readonly HashSet<string> None = [];

    private static (string Text, int Removed) Sweep(string content, IReadOnlySet<string>? covered = null)
    {
        var bytes = HostsEditor.RemoveOrphans(Encoding.UTF8.GetBytes(content), covered ?? None, out var removed);
        return (Encoding.UTF8.GetString(bytes), removed);
    }

    [Fact]
    public void Removes_lunadns_lines_the_journal_does_not_hold()
    {
        var (text, removed) = Sweep("127.0.0.1 localhost\r\n127.0.0.1 pubg.com # LunaDNS\r\n::1 a.pubg.com # LunaDNS\r\n# cuối\r\n");

        Assert.Equal("127.0.0.1 localhost\r\n# cuối\r\n", text);
        Assert.Equal(2, removed);
    }

    [Fact]
    public void Keeps_lines_the_journal_still_holds()
    {
        HashSet<string> covered = ["127.0.0.1 pubg.com # LunaDNS"];

        var (text, removed) = Sweep("127.0.0.1 pubg.com # LunaDNS\n127.0.0.1 old.pubg.com # LunaDNS\n", covered);

        Assert.Equal("127.0.0.1 pubg.com # LunaDNS\n", text);
        Assert.Equal(1, removed);
    }

    [Theory]
    [InlineData("1.2.3.4 pubg.com # LunaDNS\n")]          // không phải loopback: LunaDNS không bao giờ ghi
    [InlineData("127.0.0.1 pubg.com # LunaDNS cũ\n")]     // dấu không ở cuối dòng
    [InlineData("127.0.0.1  pubg.com # LunaDNS\n")]       // khoảng trắng khác định dạng
    [InlineData("127.0.0.1 pubg.com other.com # LunaDNS\n")]
    [InlineData("# 127.0.0.1 pubg.com # LunaDNS\n")]
    [InlineData("127.0.0.1 PUBG.com # LunaDNS\n")]
    [InlineData("127.0.0.01 pubg.com # LunaDNS\n")]       // IP không đúng dạng chuẩn LunaDNS ghi
    [InlineData("127.0.0.1 pubg.com # lunadns\n")]
    public void Never_touches_foreign_lines(string content)
    {
        var (text, removed) = Sweep(content);

        Assert.Equal(content, text);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Last_line_without_newline_is_removed_and_rest_kept_byte_for_byte()
    {
        var (text, removed) = Sweep("# máy tôi\n127.0.0.1 pubg.com # LunaDNS");

        Assert.Equal("# máy tôi\n", text);
        Assert.Equal(1, removed);
    }

    [Fact]
    public void Utf16_file_is_left_alone()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("127.0.0.1 pubg.com # LunaDNS\r\n")).ToArray();

        var result = HostsEditor.RemoveOrphans(bytes, None, out var removed);

        Assert.Equal(bytes, result);
        Assert.Equal(0, removed);
    }

    [Fact]
    public void Lines_written_by_apply_are_recognised()
    {
        var snapshot = HostsEditor.Plan("127.0.0.1 localhost\n"u8, [new("127.0.0.1", "pubg.com")]);
        var applied = HostsEditor.Apply("127.0.0.1 localhost\n"u8, snapshot);

        var result = HostsEditor.RemoveOrphans(applied, None, out var removed);

        Assert.Equal("127.0.0.1 localhost\n"u8.ToArray(), result);
        Assert.Equal(1, removed);
    }
}
