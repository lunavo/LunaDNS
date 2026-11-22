using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using LunaDns.Platform.MacOS.Processes;

namespace LunaDns.Platform.MacOS.Tests;

public sealed partial class ProcessRunnerTests
{
    private const string ChildCommand = "process-runner-sigchld-blocked";
    private const int SigBlock = 1, SigSetMask = 3;
    private const uint SigChld = 1u << (20 - 1);

    /// <summary>
    /// osascript "with administrator privileges" chạy lệnh qua authtrampoline với SIGCHLD bị chặn trong signal mask, và
    /// /bin/sh -c exec thẳng lệnh đơn nên lunadns nhận nguyên mask đó. .NET chỉ biết lệnh con đã thoát qua SIGCHLD: không
    /// được báo thì mỗi lệnh chờ đủ 15 giây rồi trả -1 (launchctl bootstrap báo lỗi dù launchd đã nạp daemon).
    /// </summary>
    [Fact]
    public void Run_SeesChildExit_WhenProcessStartedWithSigchldBlocked()
    {
        var output = RunChildWithSigchldBlocked();

        Assert.Contains("blocked=True", output, StringComparison.Ordinal);
        Assert.Contains("exit=0", output, StringComparison.Ordinal);
    }

    /// <summary>Tiến trình con (dotnet exec assembly test): nhận mask của luồng đã chặn SIGCHLD, chạy một lệnh qua ProcessRunner.</summary>
    internal static int RunChild()
    {
        BlockSignals(SigBlock, 0, out var mask);
        var watch = Stopwatch.StartNew();
        var result = new ProcessRunner().Run("/usr/bin/true", []);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"blocked={(mask & SigChld) != 0} exit={result.ExitCode} seconds={watch.Elapsed.TotalSeconds:0.0} {result.Output.Trim()}"));
        return 0;
    }

    private static string RunChildWithSigchldBlocked()
    {
        var info = new ProcessStartInfo(DotnetHost()) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("exec");
        info.ArgumentList.Add(typeof(ProcessRunnerTests).Assembly.Location);
        info.ArgumentList.Add(ChildCommand);

        // Tiến trình con thừa hưởng signal mask của luồng gọi fork: chặn SIGCHLD trên một luồng riêng rồi mới khởi động.
        Process? child = null;
        var starter = new Thread(() =>
        {
            BlockSignals(SigBlock, SigChld, out var previous);
            try
            {
                child = Process.Start(info);
            }
            finally
            {
                BlockSignals(SigSetMask, previous, out _);
            }
        });
        starter.Start();
        starter.Join();

        using var process = child ?? throw new InvalidOperationException("Không chạy được tiến trình con.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(60)), "Tiến trình con không thoát.");
        return stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
    }

    private static string DotnetHost() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
            ? host
            : Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", "dotnet"));

    private static void BlockSignals(int how, uint set, out uint previous)
    {
        if (SetThreadMask(how, in set, out previous) != 0) throw new InvalidOperationException("pthread_sigmask lỗi.");
    }

    [LibraryImport("libc", EntryPoint = "pthread_sigmask")]
    private static partial int SetThreadMask(int how, in uint set, out uint previous);
}
