using Avalonia;
using Avalonia.Controls;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// Điểm vào chung. Phần riêng nền tảng (khoá một-bản-chạy, nơi giữ tính năng, lệnh --cleanup/--install-system/--uninstall
/// chỉ có trên Windows) nằm trong Windows/Program.Windows.cs và MacOS/Program.MacOS.cs.
/// </summary>
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
        return RunPlatform(paths, args);
    }

    /// <summary>Chạy giao diện tới khi thoát, rồi hoàn tác lần cuối (idempotent nếu QuitAsync đã chạy).</summary>
    private static int RunGui(string[] args, IAppInstance instance, AppServices services)
    {
        try
        {
            return AppBuilder.Configure(() => new App { Services = services, Instance = instance })
                .UsePlatformDetect()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            // Chạy trên thread pool: xem ghi chú "Luồng" của Task 7.
            Task.Run(() => services.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Không dựng AppServices: DisposeAsync của nó hoàn tác journal, kể cả khi app giao diện đang chạy.
    /// Tự chạy "Kiểm tra" tối đa 20 giây (KH5 quyết định 15); hết giờ thì báo cáo ghi "(chưa chạy)".
    /// </summary>
    private static int RunReport(AppPaths paths, Func<IPlatformDiagnostics> diagnostics)
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
            ReportWriter.Write(paths, probe, diagnostics());
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
}
