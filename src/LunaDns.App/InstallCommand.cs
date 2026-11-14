using System.ComponentModel;
using LunaDns.Platform.Windows.Install;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// LunaDNS.exe --install-system (Setup gọi sau khi chép file): ACL thư mục dữ liệu, rồi đăng ký task SYSTEM (spec 4.6–4.7).
/// Mã thoát: 0 = xong; 1 = lỗi (xem log); 3 = exe không nằm dưới Program Files, không đổi gì (KH7 K7).
/// </summary>
internal static partial class InstallCommand
{
    public const int WrongLocation = 3;

    public static int Run(AppPaths paths, string exePath, string programFiles, IDataDirectoryAcl acl, ICleanupTaskStore tasks, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(acl);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);
        if (!InstallLocation.IsUnder(exePath, programFiles))
        {
            LogWrongLocation(logger);
            return WrongLocation;
        }
        try
        {
            // ACL trước: XML của task được ghi vào chính thư mục này.
            acl.Apply(paths.DataDirectory);
            tasks.Register(exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            LogFailed(logger, ex);
            return 1;
        }
        LogInstalled(logger);
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "--install-system: exe không nằm trong Program Files, bỏ qua.")]
    private static partial void LogWrongLocation(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--install-system: không cài được phần hệ thống.")]
    private static partial void LogFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "--install-system: đã đặt ACL và đăng ký task dọn dẹp.")]
    private static partial void LogInstalled(ILogger logger);
}
