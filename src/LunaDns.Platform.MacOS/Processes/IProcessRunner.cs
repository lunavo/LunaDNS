namespace LunaDns.Platform.MacOS.Processes;

/// <summary>Kết quả một lệnh hệ thống: mã thoát và stdout/stderr đã gộp.</summary>
public sealed record ProcessResult(int ExitCode, string Output);

/// <summary>Chạy lệnh hệ thống (networksetup, dscacheutil...). Tách ra để test không đụng máy thật.</summary>
public interface IProcessRunner
{
    ProcessResult Run(string fileName, IReadOnlyList<string> arguments);
}
