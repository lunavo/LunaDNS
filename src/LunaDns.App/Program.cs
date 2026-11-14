using Avalonia;
using System.Security.AccessControl;
using Avalonia.Controls;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Platform.Windows;
using LunaDns.Platform.Windows.Diagnostics;
using LunaDns.Platform.Windows.Install;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

internal static partial class Program
{
    private static readonly TimeSpan ReportProbeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Mã thoát: 0 = xong, 1 = còn việc chưa xong (xem log), 2 = tham số sai (WinExe không có console để in cách dùng),
    /// 3 = --install-system ngoài Program Files, 4 = --uninstall khi app đang chạy.
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = AppPaths.Default();
        paths.EnsureCreated();
        // Trước mọi lệnh và trước khi đọc journal (KH7 K9). Lỗi không chặn; giao diện ghi log.
        var aclError = TryProtectDataDirectory(paths);
        return CommandLine.Parse(args) switch
        {
            CommandKind.Gui => RunGui(paths, args, aclError),
            CommandKind.Cleanup => RunCleanup(paths),
            CommandKind.Report => RunReport(paths),
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

    private static int RunGui(AppPaths paths, string[] args, Exception? aclError)
    {
        // Mutex lấy và trả trên luồng chính (thread affinity); lấy TRƯỚC khi AppServices hoàn tác journal còn sót.
        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            SingleInstance.SignalShow();
            return 0;
        }
        var services = AppServices.CreateDefault(paths);
        if (aclError is not null) LogAclFailed(services.LoggerFactory.CreateLogger(nameof(Program)), aclError);
        try
        {
            return AppBuilder.Configure(() => new App { Services = services, Instance = instance })
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            // Hoàn tác lần cuối (idempotent nếu QuitAsync đã chạy). Chạy trên thread pool: xem ghi chú "Luồng" của Task 7.
            Task.Run(() => services.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
    }

    private static int RunCleanup(AppPaths paths)
    {
        using var loggerFactory = AppServices.CreateLoggerFactory(paths);
        return CleanupCommand.Run(paths, () => WindowsNetworkConfigurator.CreateDefault(loggerFactory),
            loggerFactory.CreateLogger(nameof(CleanupCommand)));
    }

    /// <summary>
    /// Không dựng AppServices: DisposeAsync của nó hoàn tác journal, kể cả khi app giao diện đang chạy.
    /// Tự chạy "Kiểm tra" tối đa 20 giây (KH5 quyết định 15); hết giờ thì báo cáo ghi "(chưa chạy)".
    /// </summary>
    private static int RunReport(AppPaths paths)
    {
        using var loggerFactory = AppServices.CreateLoggerFactory(paths);
        var logger = loggerFactory.CreateLogger(nameof(Program));
        var settings = new JsonFileSettingsStore(paths.SettingsFile).Load();
        IReadOnlyList<ProbeResult>? probe = null;
        using (var timeout = new CancellationTokenSource(ReportProbeTimeout))
        {
            try
            {
                probe = Task.Run(() => new ProbeRunner(() => settings, loggerFactory).RunAsync(timeout.Token)).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                LogReportProbeTimedOut(logger);
            }
        }
        try
        {
            ReportWriter.Write(paths, probe, WindowsDiagnostics.CreateDefault());
            LogReportWritten(logger);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReportFailed(logger, ex);
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "--report: kiểm tra quá 20 giây, báo cáo không có kết quả kiểm tra.")]
    private static partial void LogReportProbeTimedOut(ILogger logger);

    // Không log đường dẫn: nó chứa tên user, mà log lại được đưa vào báo cáo sau này.
    [LoggerMessage(Level = LogLevel.Information, Message = "--report: đã ghi báo cáo ra Desktop.")]
    private static partial void LogReportWritten(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "--report: không ghi được báo cáo.")]
    private static partial void LogReportFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không đặt được ACL cho thư mục dữ liệu; chạy lại Setup hoặc LunaDNS bằng quyền Admin.")]
    private static partial void LogAclFailed(ILogger logger, Exception error);
}
