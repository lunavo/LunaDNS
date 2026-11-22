using System.Globalization;
using LunaDns.Platform.MacOS.Processes;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>
/// Ai được điều khiển daemon: root, hoặc người đang ngồi trước máy (chủ của /dev/console). Người dùng SSH vào hay
/// tiến trình của tài khoản khác thì không. Giống Windows: chỉ người đã qua UAC mới bật/tắt được.
/// </summary>
public sealed class ConsoleUser(IProcessRunner runner)
{
    public uint? CurrentUid()
    {
        var result = runner.Run("/usr/bin/stat", ["-f", "%u", "/dev/console"]);
        return result.ExitCode == 0 && uint.TryParse(result.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var uid)
            ? uid
            : null;
    }

    /// <summary>Tên tài khoản đang ngồi trước máy (để che trong log trả cho giao diện); null khi không đọc được.</summary>
    public string? CurrentName()
    {
        var result = runner.Run("/usr/bin/stat", ["-f", "%Su", "/dev/console"]);
        var name = result.Output.Trim();
        return result.ExitCode == 0 && name.Length > 0 ? name : null;
    }

    public bool IsAllowed(uint uid) => uid == 0 || uid == CurrentUid();
}
