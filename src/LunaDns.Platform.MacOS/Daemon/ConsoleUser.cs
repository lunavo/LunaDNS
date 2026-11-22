using System.Runtime.InteropServices;
using System.Text;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>Người đang ngồi trước máy. Name null khi hệ điều hành không cho tên.</summary>
public sealed record ConsoleOwner(uint Uid, string? Name);

/// <summary>
/// Ai được điều khiển daemon: root, hoặc người đang ngồi trước máy (người dùng console). Người dùng SSH vào hay
/// tiến trình của tài khoản khác (Fast User Switching) thì không. Giống Windows: chỉ người đã qua UAC mới bật/tắt được.
/// Mặc định hỏi thẳng hệ điều hành (SCDynamicStoreCopyConsoleUser) cho mỗi kết nối, không sinh tiến trình (B11).
/// </summary>
public sealed partial class ConsoleUser(Func<ConsoleOwner?> read)
{
    /// <summary>Tên SystemConfiguration trả khi đang ở màn đăng nhập.</summary>
    private const string LoginWindow = "loginwindow";

    private const string SystemConfiguration = "/System/Library/Frameworks/SystemConfiguration.framework/SystemConfiguration";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8Encoding = 0x08000100; // kCFStringEncodingUTF8

    public ConsoleUser()
        : this(ReadNative)
    {
    }

    public uint? CurrentUid() => read()?.Uid;

    /// <summary>Tên tài khoản đang ngồi trước máy (để che trong log trả cho giao diện); null khi không đọc được.</summary>
    public string? CurrentName() =>
        read()?.Name is { } name && !string.IsNullOrWhiteSpace(name) && name != LoginWindow ? name : null;

    public bool IsAllowed(uint uid) => uid == 0 || uid == CurrentUid();

    /// <summary>null khi không có ai trước máy hoặc không gọi được (không phải macOS).</summary>
    private static ConsoleOwner? ReadNative()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var name = CopyConsoleUser(IntPtr.Zero, out var uid, out _);
        if (name == IntPtr.Zero) return null;
        try
        {
            return new ConsoleOwner(uid, ToManaged(name));
        }
        finally
        {
            Release(name);
        }
    }

    private static string? ToManaged(IntPtr cfString)
    {
        var buffer = new byte[1024]; // tên tài khoản macOS ngắn hơn nhiều
        if (GetCString(cfString, buffer, buffer.Length, Utf8Encoding) == 0) return null;
        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, length < 0 ? buffer.Length : length);
    }

    [LibraryImport(SystemConfiguration, EntryPoint = "SCDynamicStoreCopyConsoleUser")]
    private static partial IntPtr CopyConsoleUser(IntPtr store, out uint uid, out uint gid);

    [LibraryImport(CoreFoundation, EntryPoint = "CFStringGetCString")]
    private static partial byte GetCString(IntPtr cfString, [Out] byte[] buffer, nint bufferSize, uint encoding);

    [LibraryImport(CoreFoundation, EntryPoint = "CFRelease")]
    private static partial void Release(IntPtr cf);
}
