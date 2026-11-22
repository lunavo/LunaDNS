using LunaDns.Core.Diagnostics;
using LunaDns.Core.Logging;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Lệnh get-log (F3): đuôi log của daemon (thư mục root 700, người dùng không mở được), đã che tên người đang ngồi trước
/// máy và tên máy. Cắt cỡ để câu trả lời luôn vừa một dòng giao thức: JSON thoát ký tự ngoài ASCII thành \uXXXX (6 byte),
/// nên <see cref="MaxTotalChars"/> × 6 cộng phần trạng thái vẫn dưới <see cref="DaemonProtocol.MaxLineBytes"/>.
/// </summary>
public sealed class DaemonLogReader(string directory, Func<string?> consoleUserName, string machineName)
{
    public const int MaxLines = DiagnosticReport.LogLines;
    public const int MaxLineChars = 1000;
    public const int MaxTotalChars = 16000;

    /// <summary><paramref name="requested"/> null: <see cref="MaxLines"/>; ngoài 1…MaxLines thì kẹp lại. Mới nhất ở cuối.</summary>
    public IReadOnlyList<string> Tail(int? requested)
    {
        var count = Math.Clamp(requested ?? MaxLines, 1, MaxLines);
        IReadOnlyList<string> raw;
        try
        {
            raw = RollingFileLoggerProvider.ReadTail(directory, count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            raw = [$"(không đọc được log: {ex.Message})"];
        }

        var redactor = new Redactor(consoleUserName() ?? string.Empty, machineName);
        var kept = new List<string>(raw.Count);
        var total = 0;
        for (var i = raw.Count - 1; i >= 0; i--)
        {
            var line = Cut(redactor.Redact(raw[i]));
            if (total + line.Length > MaxTotalChars) break;
            total += line.Length;
            kept.Add(line);
        }
        kept.Reverse();
        return kept;
    }

    private static string Cut(string line) => line.Length <= MaxLineChars ? line : string.Concat(line.AsSpan(0, MaxLineChars - 1), "…");
}
