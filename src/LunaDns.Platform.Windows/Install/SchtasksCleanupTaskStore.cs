using System.Diagnostics;
using System.Text;

namespace LunaDns.Platform.Windows.Install;

/// <summary>Đăng ký, kiểm và xoá task dọn dẹp (spec 4.7).</summary>
public interface ICleanupTaskStore
{
    bool Exists();

    /// <summary>Tạo hoặc thay task chạy exePath --cleanup bằng SYSTEM. Lỗi ném InvalidOperationException.</summary>
    void Register(string exePath);

    /// <summary>Xoá nếu có; không có thì không làm gì. Lỗi ném InvalidOperationException.</summary>
    void Delete();
}

/// <summary>
/// Gọi %SystemRoot%\System32\schtasks.exe (đường dẫn tuyệt đối, không tra PATH). XML tạm ghi trong xmlDirectory —
/// phải là %ProgramData%\LunaDNS đã ACL (KH7 K9), không phải %TEMP%: ai tráo được XML là có task SYSTEM.
/// </summary>
public sealed class SchtasksCleanupTaskStore(string xmlDirectory, string taskName = CleanupTaskDefinition.TaskName) : ICleanupTaskStore
{
    private static readonly string Schtasks = Path.Combine(Environment.SystemDirectory, "schtasks.exe");

    public bool Exists() => Run("/Query", "/TN", taskName).ExitCode == 0;

    public void Register(string exePath)
    {
        var xmlFile = Path.Combine(xmlDirectory, "cleanup-task.xml");
        File.WriteAllText(xmlFile, CleanupTaskDefinition.BuildXml(exePath), Encoding.Unicode);
        try
        {
            var result = Run("/Create", "/TN", taskName, "/XML", xmlFile, "/F");
            if (result.ExitCode != 0) throw new InvalidOperationException($"schtasks /Create trả {result.ExitCode}: {result.Error}");
        }
        finally
        {
            File.Delete(xmlFile);
        }
    }

    public void Delete()
    {
        if (!Exists()) return;
        var result = Run("/Delete", "/TN", taskName, "/F");
        if (result.ExitCode != 0) throw new InvalidOperationException($"schtasks /Delete trả {result.ExitCode}: {result.Error}");
    }

    private static (int ExitCode, string Error) Run(params string[] args)
    {
        var info = new ProcessStartInfo(Schtasks)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Không chạy được schtasks.exe.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        output.GetAwaiter().GetResult();
        return (process.ExitCode, error.GetAwaiter().GetResult().Trim());
    }
}
