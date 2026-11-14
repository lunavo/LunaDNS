using System.Security;
using LunaDns.Platform.MacOS.Daemon;
using LunaDns.Platform.MacOS.Processes;

namespace LunaDns.MacCli;

/// <summary>
/// Cài/gỡ launchd daemon, tương đương --install-system/--uninstall + tác vụ SYSTEM của Windows (spec 4.7).
/// Bản chạy được chép vào thư mục chỉ root ghi được, nên user thường không tráo được chương trình mà root sẽ chạy.
/// </summary>
internal sealed class Installer(IProcessRunner runner, Action<string> say)
{
    public const string InstallDir = "/Library/PrivilegedHelperTools/" + DaemonProtocol.Label;
    public const string PlistPath = "/Library/LaunchDaemons/" + DaemonProtocol.Label + ".plist";
    public const string CommandLink = "/usr/local/bin/lunadns";
    private const string Launchctl = "/bin/launchctl";
    private const string Xattr = "/usr/bin/xattr";

    private const UnixFileMode Exec = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode Read = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static string Executable => Path.Combine(InstallDir, "lunadns");

    public async Task<bool> InstallAsync(string sourceDir)
    {
        var source = Path.GetFullPath(sourceDir);
        if (!File.Exists(Path.Combine(source, "lunadns")))
        {
            say("Không thấy file chạy 'lunadns' cạnh chương trình. Hãy cài từ bản đã publish (xem docs/guide/09-macos.md).");
            return false;
        }
        if (string.Equals(source.TrimEnd('/'), InstallDir, StringComparison.Ordinal))
        {
            say("Đang chạy từ chính thư mục cài; chạy install từ bản tải về mới.");
            return false;
        }

        Bootout(); // daemon cũ nhận SIGTERM và tự hoàn tác
        if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, recursive: true);
        Copy(source, InstallDir);
        // Bản tải về (.dmg) mang com.apple.quarantine, File.Copy giữ nguyên thuộc tính này. Gatekeeper có thể chặn âm thầm binary
        // ký ad-hoc còn quarantine khi launchd chạy nó (không có hộp thoại "Open Anyway" cho daemon). Mã thoát khác 0 khi không có
        // thuộc tính nào: bỏ qua.
        runner.Run(Xattr, ["-dr", "com.apple.quarantine", InstallDir]);

        await File.WriteAllTextAsync(PlistPath, Plist()).ConfigureAwait(false);
        File.SetUnixFileMode(PlistPath, Read);

        var boot = runner.Run(Launchctl, ["bootstrap", "system", PlistPath]);
        if (boot.ExitCode != 0)
        {
            say($"launchctl bootstrap lỗi: {boot.Output.Trim()}");
            return false;
        }
        LinkCommand();

        for (var i = 0; i < 20; i++)
        {
            try
            {
                var pong = await new DaemonClient().SendAsync(new DaemonRequest(DaemonProtocol.Ping)).ConfigureAwait(false);
                say($"Đã cài dịch vụ nền LunaDNS {pong.Version} ({InstallDir}).");
                return true;
            }
            catch (DaemonUnavailableException)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
        }
        say("Đã cài nhưng dịch vụ nền chưa trả lời; xem /Library/Application Support/LunaDNS/logs.");
        return false;
    }

    /// <summary>Gỡ daemon (daemon tự hoàn tác khi nhận SIGTERM), file cài, lệnh tắt; dữ liệu do bên gọi xoá sau khi cleanup.</summary>
    public void Uninstall()
    {
        Bootout();
        if (File.Exists(PlistPath)) File.Delete(PlistPath);
        if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, recursive: true);
        var link = new FileInfo(CommandLink);
        if (link.LinkTarget is { } target && target.StartsWith(InstallDir, StringComparison.Ordinal)) link.Delete();
    }

    public static bool IsInstalled => File.Exists(PlistPath);

    private void Bootout()
    {
        var result = runner.Run(Launchctl, ["bootout", "system/" + DaemonProtocol.Label]);
        // Mã khác 0 khi daemon chưa nạp: bình thường. Chờ tiến trình cũ nhả khoá (đã hoàn tác xong).
        if (result.ExitCode == 0)
        {
            for (var i = 0; i < 40 && IsLoaded(); i++) Thread.Sleep(250);
        }
    }

    private bool IsLoaded() => runner.Run(Launchctl, ["print", "system/" + DaemonProtocol.Label]).ExitCode == 0;

    /// <summary>/usr/local/bin/lunadns trỏ vào bản cài, nếu thư mục có sẵn và tên chưa bị chương trình khác dùng.</summary>
    private static void LinkCommand()
    {
        if (!Directory.Exists(Path.GetDirectoryName(CommandLink))) return;
        var link = new FileInfo(CommandLink);
        if (link.Exists || link.LinkTarget is not null)
        {
            if (link.LinkTarget is not { } target || !target.StartsWith(InstallDir, StringComparison.Ordinal)) return;
            link.Delete();
        }
        File.CreateSymbolicLink(CommandLink, Executable);
    }

    private static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to, Exec);
        File.SetUnixFileMode(to, Exec);
        foreach (var dir in Directory.EnumerateDirectories(from))
            Copy(dir, Path.Combine(to, Path.GetFileName(dir)));
        foreach (var file in Directory.EnumerateFiles(from))
        {
            var dest = Path.Combine(to, Path.GetFileName(file));
            File.Copy(file, dest, overwrite: true);
            File.SetUnixFileMode(dest, Path.GetFileName(file) == "lunadns" ? Exec : Read);
        }
    }

    internal static string Plist()
    {
        var exe = SecurityElement.Escape(Executable);
        var log = SecurityElement.Escape(Path.Combine(MacPaths.DefaultRoot, "logs", "daemon.out"));
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{DaemonProtocol.Label}</string>
              <key>ProgramArguments</key>
              <array><string>{exe}</string><string>daemon</string></array>
              <key>RunAtLoad</key><true/>
              <key>KeepAlive</key><true/>
              <key>ExitTimeOut</key><integer>20</integer>
              <key>StandardOutPath</key><string>{log}</string>
              <key>StandardErrorPath</key><string>{log}</string>
            </dict>
            </plist>

            """;
    }
}
