using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace LunaDns.Core.Logging;

/// <summary>
/// Log ra file xoay vòng (spec 6): <c>lunadns.log</c> tối đa 1 MB, rồi đẩy thành lunadns.1.log … lunadns.4.log (giữ 5 file).
/// Ghi đồng bộ dưới khoá và flush từng dòng: app ghi ít, và log phải còn nguyên nếu tiến trình chết ngay sau đó.
/// </summary>
public sealed class RollingFileLoggerProvider : ILoggerProvider
{
    public const string FileName = "lunadns.log";
    public const long DefaultMaxFileBytes = 1024 * 1024;
    public const int MaxFiles = 5;

    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly long _maxFileBytes;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private FileStream? _stream;

    public RollingFileLoggerProvider(string directory, LogLevel minLevel = LogLevel.Information, long maxFileBytes = DefaultMaxFileBytes, TimeProvider? time = null)
    {
        _directory = directory;
        _minLevel = minLevel;
        _maxFileBytes = maxFileBytes;
        _time = time ?? TimeProvider.System;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary><paramref name="count"/> dòng cuối, từ file cũ nhất tới file hiện tại (cho báo cáo chẩn đoán).</summary>
    public static IReadOnlyList<string> ReadTail(string directory, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var lines = new Queue<string>(count);
        for (var index = MaxFiles - 1; index >= 0; index--)
        {
            var path = PathOf(directory, index);
            if (!File.Exists(path)) continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (reader.ReadLine() is { } line)
                {
                    if (lines.Count == count) lines.Dequeue();
                    lines.Enqueue(line);
                }
            }
            catch (FileNotFoundException)
            {
                // File vừa bị xoay vòng giữa Exists và Open: phần còn lại vẫn đọc được ở file kế.
                continue;
            }
        }
        return [.. lines];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var text = new StringBuilder()
            .Append(_time.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(Label(level)).Append(' ').Append(category).Append(": ").Append(message);
        if (exception is not null)
            text.AppendLine().Append("    ").Append(exception.ToString().ReplaceLineEndings(Environment.NewLine + "    "));
        text.AppendLine();
        var bytes = Encoding.UTF8.GetBytes(text.ToString());

        lock (_gate)
        {
            try
            {
                var stream = _stream ??= Open();
                if (stream.Length > 0 && stream.Length + bytes.Length > _maxFileBytes)
                {
                    stream.Dispose();
                    Rotate();
                    stream = _stream = Open();
                }
                stream.Write(bytes);
                stream.Flush();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Không có chỗ nào khác để ghi lỗi của log: bỏ dòng này, mở lại file ở lần ghi sau.
                _stream?.Dispose();
                _stream = null;
            }
        }
    }

    private FileStream Open() =>
        new(PathOf(_directory, 0), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

    private void Rotate()
    {
        File.Delete(PathOf(_directory, MaxFiles - 1));
        for (var index = MaxFiles - 2; index >= 0; index--)
        {
            var from = PathOf(_directory, index);
            if (File.Exists(from)) File.Move(from, PathOf(_directory, index + 1));
        }
    }

    private static string PathOf(string directory, int index) =>
        Path.Combine(directory, index == 0 ? FileName : string.Create(CultureInfo.InvariantCulture, $"lunadns.{index}.log"));

    private static string Label(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRCE",
        LogLevel.Debug => "DBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "FAIL",
        _ => "CRIT",
    };

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= provider._minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            ArgumentNullException.ThrowIfNull(formatter);
            provider.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
