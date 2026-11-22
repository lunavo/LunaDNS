using System.Diagnostics;

namespace LunaDns.App;

/// <summary>Mở URL, thư mục và chạy chương trình ngoài, để Maintenance và VM test được không cần Process thật.</summary>
internal interface ISystemShell
{
    void OpenUrl(string url);

    void StartProcess(string path);

    /// <summary>Mở thư mục trong Finder/Explorer (U2: thư mục log).</summary>
    void OpenFolder(string path);

    /// <summary>Mở Finder/Explorer và chọn sẵn file (U2: báo cáo vừa lưu).</summary>
    void Reveal(string file);
}

internal sealed class SystemShell : ISystemShell
{
    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    public void StartProcess(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();

    public void OpenFolder(string path) => Process.Start(FolderCommand(path, OperatingSystem.IsMacOS()))?.Dispose();

    public void Reveal(string file) => Process.Start(RevealCommand(file, OperatingSystem.IsMacOS()))?.Dispose();

    internal static ProcessStartInfo FolderCommand(string path, bool mac)
    {
        var info = new ProcessStartInfo(mac ? "/usr/bin/open" : "explorer.exe") { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return info;
    }

    /// <summary>explorer.exe tự tách tham số theo cách riêng: phải là một chuỗi /select,"đường dẫn", không qua ArgumentList.</summary>
    internal static ProcessStartInfo RevealCommand(string file, bool mac)
    {
        if (!mac) return new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = false };
        var info = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
        info.ArgumentList.Add("-R");
        info.ArgumentList.Add(file);
        return info;
    }
}
