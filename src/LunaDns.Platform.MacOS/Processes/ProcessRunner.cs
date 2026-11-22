using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LunaDns.Platform.MacOS.Processes;

/// <summary>Chạy lệnh bằng đường dẫn tuyệt đối, không qua shell (tham số không bị shell diễn giải). Hết 15 giây thì giết.</summary>
public sealed partial class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private const int SigUnblock = 2;
    private const uint SigChld = 1u << (20 - 1);

    public ProcessResult Run(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        UnblockChildExitSignal();
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Không chạy được {fileName}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            process.Kill(entireProcessTree: true);
            return new ProcessResult(-1, $"{fileName} quá {Timeout.TotalSeconds:0} giây không trả lời.");
        }
        return new ProcessResult(process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
    }

    /// <summary>
    /// .NET chỉ biết lệnh con đã thoát nhờ SIGCHLD. "do shell script … with administrator privileges" (cài dịch vụ nền từ giao
    /// diện) chạy lunadns với SIGCHLD bị chặn trên mọi luồng, nên WaitForExit không bao giờ thấy lệnh xong và mọi lệnh chờ đủ
    /// 15 giây rồi trả -1. Bỏ chặn trên luồng gọi: luồng này đứng chờ trong WaitForExit nên luôn sẵn sàng nhận tín hiệu.
    /// </summary>
    private static void UnblockChildExitSignal()
    {
        var set = SigChld;
        _ = SetThreadSignalMask(SigUnblock, in set, out _);
    }

    [LibraryImport("libc", EntryPoint = "pthread_sigmask")]
    private static partial int SetThreadSignalMask(int how, in uint set, out uint previous);
}
