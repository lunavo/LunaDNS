using System.Security.AccessControl;
using LunaDns.Platform.Windows;
using LunaDns.Platform.Windows.Diagnostics;
using LunaDns.Platform.Windows.Install;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

internal static partial class Program
{
    private static int RunPlatform(AppPaths paths, string[] args)
    {
        // Trước mọi lệnh và trước khi đọc journal (KH7 K9). Lỗi không chặn; giao diện ghi log.
        var aclError = TryProtectDataDirectory(paths);
        return CommandLine.Parse(args) switch
        {
            CommandKind.Gui => RunWindowsGui(paths, args, aclError),
            CommandKind.Cleanup => RunCleanup(paths),
            CommandKind.Report => RunReport(paths, () => new WindowsReportDiagnostics(WindowsDiagnostics.CreateDefault(), paths.JournalFile)),
            CommandKind.InstallSystem => RunInstallSystem(paths),
            CommandKind.Uninstall => RunUninstall(paths),
            _ => 2,
        };
    }

    private static Exception? TryProtectDataDirectory(AppPaths paths)
    {
        try
        {
            new DataDirectoryAcl().Apply(paths.DataDirectory);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or PrivilegeNotHeldException)
        {
            return ex;
        }
    }

    private static int RunInstallSystem(AppPaths paths)
    {
        using var loggerFactory = AppServices.CreateLoggerFactory(paths);
        return InstallCommand.Run(paths, Environment.ProcessPath!, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            new DataDirectoryAcl(), new SchtasksCleanupTaskStore(paths.DataDirectory), loggerFactory.CreateLogger(nameof(InstallCommand)));
    }

    private static int RunUninstall(AppPaths paths)
    {
        int code;
        using (var loggerFactory = AppServices.CreateLoggerFactory(paths))
        {
            code = UninstallCommand.Run(paths, () => WindowsNetworkConfigurator.CreateDefault(loggerFactory),
                new SchtasksCleanupTaskStore(paths.DataDirectory), loggerFactory.CreateLogger(nameof(UninstallCommand)));
        }
        // Log nằm trong thư mục dữ liệu: đóng logger rồi mới xoá.
        if (code == 0 && !UninstallCommand.TryDeleteData(paths)) code = 1;
        return code;
    }

    private static int RunWindowsGui(AppPaths paths, string[] args, Exception? aclError)
    {
        // Mutex lấy và trả trên luồng chính (thread affinity); lấy TRƯỚC khi AppServices hoàn tác journal còn sót.
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalShow();
            return 0;
        }
        var loggerFactory = AppServices.CreateLoggerFactory(paths);
        var services = new AppServices(paths, WindowsNetworkConfigurator.CreateDefault(loggerFactory), loggerFactory);
        if (aclError is not null) LogAclFailed(loggerFactory.CreateLogger(nameof(Program)), aclError);
        return RunGui(args, instance, services);
    }

    private static int RunCleanup(AppPaths paths)
    {
        using var loggerFactory = AppServices.CreateLoggerFactory(paths);
        return CleanupCommand.Run(paths, () => WindowsNetworkConfigurator.CreateDefault(loggerFactory),
            loggerFactory.CreateLogger(nameof(CleanupCommand)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không đặt được ACL cho thư mục dữ liệu; chạy lại Setup hoặc LunaDNS bằng quyền Admin.")]
    private static partial void LogAclFailed(ILogger logger, Exception error);
}
