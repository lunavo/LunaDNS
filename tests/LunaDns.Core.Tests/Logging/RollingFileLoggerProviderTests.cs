using LunaDns.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LunaDns.Core.Tests.Logging;

public sealed class RollingFileLoggerProviderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lunadns-logs-").FullName;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private RollingFileLoggerProvider Create(long maxFileBytes = RollingFileLoggerProvider.DefaultMaxFileBytes) =>
        new(_dir, LogLevel.Information, maxFileBytes, _time);

    // Gọi ILogger.Log trực tiếp để không phụ thuộc analyzer của các extension LogInformation/LogDebug.
    private static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null) =>
        logger.Log(level, default, message, exception, (s, _) => s);

    private string ReadCurrent() => File.ReadAllText(Path.Combine(_dir, RollingFileLoggerProvider.FileName));

    [Fact]
    public void Writes_time_level_category_and_message()
    {
        using (var provider = Create()) Write(provider.CreateLogger("Test"), LogLevel.Information, "xin chào");

        Assert.Equal("2026-11-13 08:00:00.000 INFO Test: xin chào" + Environment.NewLine, ReadCurrent());
    }

    [Fact]
    public void Skips_levels_below_minimum()
    {
        using (var provider = Create())
        {
            var logger = provider.CreateLogger("Test");
            Write(logger, LogLevel.Debug, "ẩn");
            Write(logger, LogLevel.Warning, "hiện");
        }

        var text = ReadCurrent();
        Assert.DoesNotContain("ẩn", text, StringComparison.Ordinal);
        Assert.Contains("WARN Test: hiện", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Writes_exception_indented_on_following_lines()
    {
        using (var provider = Create()) Write(provider.CreateLogger("Test"), LogLevel.Error, "hỏng", new InvalidOperationException("lý do"));

        Assert.Contains(Environment.NewLine + "    System.InvalidOperationException: lý do", ReadCurrent(), StringComparison.Ordinal);
    }

    [Fact]
    public void Rotates_and_keeps_five_files()
    {
        using (var provider = Create(maxFileBytes: 100))
        {
            var logger = provider.CreateLogger("T");
            for (var i = 0; i < 50; i++) Write(logger, LogLevel.Information, $"dòng {i}");
        }

        Assert.Equal(5, Directory.GetFiles(_dir, "lunadns*.log").Length);
        Assert.True(File.Exists(Path.Combine(_dir, "lunadns.4.log")));
        Assert.False(File.Exists(Path.Combine(_dir, "lunadns.5.log")));
    }

    [Fact]
    public void Read_tail_returns_last_lines_across_files_in_order()
    {
        using (var provider = Create(maxFileBytes: 100))
        {
            var logger = provider.CreateLogger("T");
            for (var i = 0; i < 30; i++) Write(logger, LogLevel.Information, $"dòng {i}");
        }

        var tail = RollingFileLoggerProvider.ReadTail(_dir, 5);

        Assert.Equal(5, tail.Count);
        for (var i = 0; i < 5; i++) Assert.EndsWith($"dòng {25 + i}", tail[i], StringComparison.Ordinal);
    }

    [Fact]
    public void Read_tail_of_empty_directory_is_empty() =>
        Assert.Empty(RollingFileLoggerProvider.ReadTail(_dir, 200));
}
