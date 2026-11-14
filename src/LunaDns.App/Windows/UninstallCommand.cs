using System.ComponentModel;
using LunaDns.Core.Platform;
using LunaDns.Platform.Windows.Install;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// LunaDNS.exe --uninstall (trình gỡ của Setup gọi; bản portable chạy tay): hoàn tác journal, xoá task SYSTEM.
/// Hoàn tác lỗi thì giữ journal và task để lần sau làm tiếp. Xoá thư mục dữ liệu là bước riêng (TryDeleteData),
/// chạy sau khi đóng logger vì log nằm trong thư mục đó.
/// Mã thoát: 0 = xong; 1 = còn việc chưa xong (xem log); 4 = LunaDNS đang chạy.
/// </summary>
internal static partial class UninstallCommand
{
    public const int AppRunning = 4;

    public static int Run(AppPaths paths, Func<INetworkConfigurator> configurator, ICleanupTaskStore tasks, ILogger logger, string mutexName = SingleInstance.DefaultName)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(logger);
        using var instance = SingleInstance.TryAcquire(mutexName);
        if (instance is null)
        {
            LogAppRunning(logger);
            return AppRunning;
        }
        // Không gọi CleanupCommand.Run: nó lấy lại mutex đang giữ, nhận null và bỏ qua (xem SingleInstance).
        if (CleanupCommand.RollbackJournal(paths, configurator, logger) != 0)
        {
            LogKept(logger);
            return 1;
        }
        try
        {
            tasks.Delete();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception)
        {
            LogTaskFailed(logger, ex);
            return 1;
        }
        LogDone(logger);
        return 0;
    }

    public static bool TryDeleteData(AppPaths paths)
    {
        try
        {
            if (Directory.Exists(paths.DataDirectory)) Directory.Delete(paths.DataDirectory, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "--uninstall: LunaDNS đang chạy, không gỡ.")]
    private static partial void LogAppRunning(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--uninstall: còn mục chưa hoàn tác, giữ journal và task dọn dẹp.")]
    private static partial void LogKept(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--uninstall: không xoá được task dọn dẹp.")]
    private static partial void LogTaskFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Information, Message = "--uninstall: đã hoàn tác và xoá task dọn dẹp.")]
    private static partial void LogDone(ILogger logger);
}
