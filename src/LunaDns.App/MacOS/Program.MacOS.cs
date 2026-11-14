using LunaDns.App.MacOS;
using LunaDns.Platform.MacOS;

namespace LunaDns.App;

/// <summary>
/// macOS: giao diện chạy quyền user, không đổi hệ thống; mọi bật/tắt đi qua dịch vụ nền (DaemonFeatureController).
/// --cleanup/--install-system/--uninstall là lệnh của Windows: trên macOS dùng <c>sudo lunadns cleanup|install|uninstall</c>.
/// </summary>
internal static partial class Program
{
    private static int RunPlatform(AppPaths paths, string[] args) => CommandLine.Parse(args) switch
    {
        CommandKind.Gui => RunMacGui(paths, args),
        CommandKind.Report => RunReport(paths, () => CreateMacDiagnostics(null)),
        _ => 2,
    };

    private static int RunMacGui(AppPaths paths, string[] args)
    {
        using var instance = MacAppInstance.TryAcquire(paths.DataDirectory);
        if (instance is null)
        {
            MacAppInstance.SignalShow(paths.DataDirectory);
            return 0;
        }
        var loggerFactory = AppServices.CreateLoggerFactory(paths);
        var services = new AppServices(paths, _ => DaemonFeatureController.CreateDefault(loggerFactory), loggerFactory);
        return RunGui(args, instance, services);
    }

    /// <summary>daemon null (lệnh --report): tự hỏi dịch vụ nền một lần để biết phiên bản và số mục journal.</summary>
    internal static MacReportDiagnostics CreateMacDiagnostics(DaemonFeatureController? daemon)
    {
        if (daemon is null)
        {
            daemon = DaemonFeatureController.CreateDefault(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            Task.Run(() => daemon.RefreshAsync()).GetAwaiter().GetResult();
        }
        return new MacReportDiagnostics(MacNetworkConfigurator.CreateDefault(), daemon);
    }
}
