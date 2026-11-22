using System.Runtime.InteropServices;

namespace LunaDns.Platform.MacOS.AppleEvents;

/// <summary>
/// Phân biệt Cmd+Q / Quit trên Dock với đăng xuất / khởi động lại / tắt máy (spec 2026-11-22 F1). Gọi trên luồng chính, ngay trong
/// lúc app đang xử lý yêu cầu thoát (applicationShouldTerminate): loginwindow gửi Apple Event <c>quit</c> kèm thuộc tính
/// <c>keyAEQuitReason</c>; Cmd+Q từ menu không có Apple Event, <c>osascript … quit</c> có nhưng không kèm lý do.
/// </summary>
public static partial class QuitReason
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const uint KeyAEQuitReason = 0x7768793F; // 'why?'

    /// <summary>kAELogOut, kAEReallyLogOut, kAEShowRestartDialog, kAERestart, kAEShowShutdownDialog, kAEShutDown.</summary>
    private static readonly uint[] SessionEndReasons =
        [FourCc("logo"), FourCc("rlgo"), FourCc("rrst"), FourCc("rest"), FourCc("rsdn"), FourCc("shut")];

    /// <summary>true khi hệ thống đang đăng xuất/tắt máy. Đọc lỗi thì false (coi như người dùng thoát).</summary>
    public static bool IsSessionEnding()
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            return IsSessionEndReason(CurrentReason());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            return false;
        }
    }

    /// <summary>Mã lý do của Apple Event đang xử lý; 0 nếu không có Apple Event hoặc không kèm lý do.</summary>
    public static uint CurrentReason()
    {
        var manager = Send(GetClass("NSAppleEventManager"), Selector("sharedAppleEventManager"));
        if (manager == IntPtr.Zero) return 0;
        var current = Send(manager, Selector("currentAppleEvent"));
        if (current == IntPtr.Zero) return 0;
        var reason = SendKeyword(current, Selector("attributeDescriptorForKeyword:"), KeyAEQuitReason);
        return reason == IntPtr.Zero ? 0 : SendUInt(reason, Selector("enumCodeValue"));
    }

    public static bool IsSessionEndReason(uint reason) => Array.IndexOf(SessionEndReasons, reason) >= 0;

    public static uint FourCc(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 4) throw new ArgumentException("Cần đúng 4 ký tự.", nameof(code));
        return ((uint)code[0] << 24) | ((uint)code[1] << 16) | ((uint)code[2] << 8) | code[3];
    }

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr GetClass(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial IntPtr SendKeyword(IntPtr receiver, IntPtr selector, uint keyword);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial uint SendUInt(IntPtr receiver, IntPtr selector);
}
