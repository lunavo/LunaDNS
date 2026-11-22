using System.Text;
using System.Text.Json;
using LunaDns.Core.Logging;
using LunaDns.Platform.MacOS.Daemon;

namespace LunaDns.Platform.MacOS.Tests;

/// <summary>F3: get-log trả đuôi log của daemon, đã che tên và luôn vừa một dòng JSON của giao thức.</summary>
public sealed class DaemonLogTests : IDisposable
{
    private readonly TempDirectory _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string LogFile => _tmp.Combine(RollingFileLoggerProvider.FileName);

    private DaemonLogReader Reader(string? user = "lunav") => new(_tmp.Path, () => user, "Lunas-MacBook");

    [Fact]
    public void Default_and_maximum_is_two_hundred_newest_lines()
    {
        File.WriteAllLines(LogFile, Enumerable.Range(1, 250).Select(i => $"dòng {i}"));

        var all = Reader().Tail(null);
        Assert.Equal(DaemonLogReader.MaxLines, all.Count);
        Assert.Equal("dòng 250", all[^1]);
        Assert.Equal("dòng 51", all[0]);

        Assert.Equal(DaemonLogReader.MaxLines, Reader().Tail(5000).Count);
        Assert.Equal(["dòng 249", "dòng 250"], Reader().Tail(2));
        Assert.Single(Reader().Tail(0));
    }

    [Fact]
    public void User_and_machine_names_are_redacted()
    {
        File.WriteAllLines(LogFile, ["WARN /Users/lunav trên Lunas-MacBook, IP 192.168.1.20"]);

        Assert.Equal(["WARN /Users/<user> trên <host>, IP 192.168.x.x"], Reader().Tail(null));
    }

    [Fact]
    public void Unknown_console_user_still_redacts_machine_name()
    {
        File.WriteAllLines(LogFile, ["Lunas-MacBook"]);

        Assert.Equal(["<host>"], Reader(user: null).Tail(null));
    }

    [Fact]
    public void Missing_directory_gives_no_lines() =>
        Assert.Empty(new DaemonLogReader(_tmp.Combine("không-có"), () => "lunav", "host").Tail(null));

    [Fact]
    public void Long_lines_are_cut_and_the_reply_fits_one_protocol_line()
    {
        // Ký tự ngoài ASCII bị JSON thoát thành \uXXXX (6 byte): trường hợp xấu nhất cho cỡ câu trả lời.
        File.WriteAllLines(LogFile, Enumerable.Range(1, 250).Select(i => i + new string('ệ', 5000)));

        var lines = Reader().Tail(null);

        Assert.All(lines, l => Assert.True(l.Length <= DaemonLogReader.MaxLineChars));
        Assert.EndsWith("…", lines[^1], StringComparison.Ordinal);
        Assert.StartsWith("250", lines[^1], StringComparison.Ordinal);
        Assert.True(lines.Sum(l => l.Length) <= DaemonLogReader.MaxTotalChars);
        var reply = new DaemonResponse(true, Version: "0.10.1", Status: new DaemonStatus(true, true, 18080, 3, 4), Log: [.. lines]);
        var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(reply, DaemonProtocol.ResponseInfo) + "\n");
        Assert.True(bytes < DaemonProtocol.MaxLineBytes, $"{bytes} byte");
    }
}
