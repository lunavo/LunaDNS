using System.Diagnostics;

namespace LunaDns.Platform.MacOS.Processes;

/// <summary>Chạy lệnh bằng đường dẫn tuyệt đối, không qua shell (tham số không bị shell diễn giải). Hết 15 giây thì giết.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

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
}
