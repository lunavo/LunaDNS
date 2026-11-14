using System.Diagnostics;

namespace LunaDns.App;

/// <summary>Mở URL và chạy chương trình ngoài, để Maintenance test được không cần Process thật.</summary>
internal interface ISystemShell
{
    void OpenUrl(string url);

    void StartProcess(string path);
}

internal sealed class SystemShell : ISystemShell
{
    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    public void StartProcess(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
}
