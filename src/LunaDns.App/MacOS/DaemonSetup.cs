using System.Diagnostics;

using LunaDns.App.Features;

namespace LunaDns.App.MacOS;

/// <summary>
/// Cài dịch vụ nền từ giao diện: <c>osascript</c> hiện hộp thoại mật khẩu của macOS rồi chạy <c>lunadns install</c> bằng root
/// (không cần Developer ID như SMAppService). Đường dẫn truyền qua argv của AppleScript và <c>quoted form of</c>, không ghép chuỗi.
/// </summary>
internal sealed class DaemonSetup(Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>>? run = null)
{
    public const string Osascript = "/usr/bin/osascript";

    /// <summary>Mã lỗi AppleScript khi người dùng bấm Huỷ ở hộp thoại mật khẩu.</summary>
    private const string UserCancelled = "-128";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<(int ExitCode, string Output)>> _run = run ?? RunProcessAsync;

    /// <summary>
    /// Bản chạy <c>lunadns</c> đi kèm giao diện: <c>&lt;thư mục app&gt;/daemon/lunadns</c> (bố cục .app của Bước 4 giữ đúng chỗ này;
    /// khi chạy từ mã nguồn, build/run-macos.sh publish CLI vào đó). null nếu không có.
    /// </summary>
    public static string? FindCli(string baseDirectory)
    {
        var path = Path.Combine(baseDirectory, "daemon", "lunadns");
        return File.Exists(path) ? path : null;
    }

    public static IReadOnlyList<string> Arguments(string cliPath) =>
    [
        "-e", "on run argv",
        "-e", "do shell script (quoted form of item 1 of argv) & \" install\" with administrator privileges",
        "-e", "end run",
        cliPath,
    ];

    public async Task<ServiceInstallResult> InstallAsync(string cliPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cliPath);
        var (code, output) = await _run(Osascript, Arguments(cliPath), cancellationToken).ConfigureAwait(false);
        return new ServiceInstallResult(code == 0, code != 0 && output.Contains(UserCancelled, StringComparison.Ordinal), output.Trim());
    }

    /// <summary>Không dùng ProcessRunner của Platform.MacOS: nó giết lệnh sau 15 giây, còn người dùng có thể gõ mật khẩu lâu hơn.</summary>
    private static async Task<(int, string)> RunProcessAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(fileName) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Không chạy được {fileName}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return (-1, "osascript quá thời gian chờ.");
        }
        return (process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
    }
}
