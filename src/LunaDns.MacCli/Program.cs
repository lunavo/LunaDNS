using System.Reflection;
using System.Runtime.InteropServices;
using LunaDns.Core.Features;
using LunaDns.Core.Hosts;
using LunaDns.Core.Journal;
using LunaDns.MacCli;
using LunaDns.Platform.MacOS;
using LunaDns.Platform.MacOS.Daemon;
using LunaDns.Platform.MacOS.Processes;
using Microsoft.Extensions.Logging;

// CLI macOS. Hai cách chạy:
//  - Có dịch vụ nền (khuyên dùng): sudo lunadns install một lần, sau đó lunadns on/off/status không cần sudo.
//  - Không cài: sudo lunadns run, giữ Terminal mở.
const int Ok = 0, Pending = 1, BadArgs = 2;

var command = args.Length > 0 ? args[0] : "help";
var options = args.Skip(1).ToHashSet(StringComparer.Ordinal);
var version = typeof(Engine).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
return command switch
{
    "on" => await OnAsync(options.Contains("--proxy"), options.Contains("--allow-proxy-override")),
    "off" => await SendAsync(new DaemonRequest(DaemonProtocol.DisableAll)),
    "status" => await StatusAsync(),
    "run" => await RunAsync(options.Contains("--proxy"), options.Contains("--allow-proxy-override")),
    "daemon" => await DaemonAsync(version),
    "cleanup" => Cleanup(),
    "install" => await InstallAsync(),
    "uninstall" => await UninstallAsync(),
    "help" or "--help" or "-h" => Help(Ok),
    _ => Help(BadArgs),
};

static int Help(int code)
{
    Console.WriteLine("""
        LunaDNS cho macOS (bản dòng lệnh thử nghiệm)

        Dùng dịch vụ nền (cài một lần, tự dọn sau khi khởi động lại máy):
          sudo lunadns install   Cài dịch vụ nền (launchd) vào /Library/PrivilegedHelperTools.
          lunadns on [--proxy] [--allow-proxy-override]
                                 Bật Định tuyến DNS; --proxy bật thêm Proxy SNI. Không cần sudo.
          lunadns off            Tắt hết và khôi phục.
          lunadns status         Xem LunaDNS đang đổi gì trên máy.
          sudo lunadns uninstall Tắt, khôi phục, gỡ dịch vụ nền và dữ liệu.

        Không cài:
          sudo lunadns run [--proxy] [--allow-proxy-override]
                                 Như "on" nhưng chạy tới khi Ctrl+C.
          sudo lunadns cleanup   Hoàn tác mọi thay đổi còn trong journal.

        Mã thoát: 0 xong, 1 còn việc chưa xong (xem log), 2 tham số sai hoặc thiếu quyền.
        """);
    return code;
}

static bool RequireRoot()
{
    if (Environment.UserName == "root") return true;
    Console.Error.WriteLine("Lệnh này đổi cấu hình hệ thống, cần chạy bằng sudo.");
    return false;
}

static void PrintRollback(RollbackResult result)
{
    if (result.Reverted > 0) Console.WriteLine($"Đã hoàn tác {result.Reverted} thay đổi.");
    foreach (var failure in result.Failed)
        Console.Error.WriteLine($"Chưa hoàn tác được {failure.Entry.Op} ({failure.Entry.Feature}): {failure.Error.Message}");
}

static void PrintStatus(DaemonStatus s)
{
    Console.WriteLine($"Định tuyến DNS: {(s.DnsRouting ? $"bật (127.0.0.1:{MacNetworkConfigurator.DefaultDnsPort})" : "tắt")}");
    Console.WriteLine($"Proxy SNI: {(s.SniProxy ? $"bật (127.0.0.1:{s.ProxyPort}, {s.ProxyConnections} kết nối)" : "tắt")}");
    Console.WriteLine($"Journal: {s.JournalEntries} mục");
}

static async Task<int> SendAsync(DaemonRequest request)
{
    try
    {
        var response = await new DaemonClient().SendAsync(request);
        if (response.Message is { } message) (response.Ok ? Console.Out : Console.Error).WriteLine(message);
        if (response.Status is { } status) PrintStatus(status);
        return response.Ok ? Ok : Pending;
    }
    catch (DaemonUnavailableException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return Pending;
    }
}

static async Task<int> OnAsync(bool proxy, bool allowProxyOverride)
{
    var code = await SendAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.DnsRouting));
    if (code != Ok || !proxy) return code;
    return await SendAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.SniProxy, allowProxyOverride));
}

/// <summary>Có daemon thì hỏi daemon; không có thì tự đọc /etc/resolver, networksetup, /etc/hosts.</summary>
static async Task<int> StatusAsync()
{
    try
    {
        var response = await new DaemonClient().SendAsync(new DaemonRequest(DaemonProtocol.Status));
        Console.WriteLine($"Dịch vụ nền: đang chạy ({response.Version})");
        if (response.Status is { } status) PrintStatus(status);
        else if (response.Message is { } message) Console.Error.WriteLine(message);
        return response.Ok ? Ok : Pending;
    }
    catch (DaemonUnavailableException)
    {
        Console.WriteLine(Installer.IsInstalled ? "Dịch vụ nền: đã cài nhưng không trả lời" : "Dịch vụ nền: chưa cài");
    }

    var configurator = MacNetworkConfigurator.CreateDefault();
    var resolver = configurator.Dns.ListOurs();
    Console.WriteLine(resolver.Count == 0
        ? "Định tuyến DNS: tắt"
        : $"Định tuyến DNS: bật ({string.Join(", ", resolver)} → 127.0.0.1:{configurator.Dns.Port})");
    try
    {
        var services = configurator.Proxy.ServicesUsingLunaDnsPac();
        Console.WriteLine(services.Count == 0 ? "Proxy SNI: tắt" : $"Proxy SNI: bật trên {string.Join(", ", services)}");
    }
    catch (LunaDns.Core.Platform.ConfiguratorException ex)
    {
        Console.WriteLine($"Proxy SNI: không đọc được ({ex.Message})");
    }
    var hosts = System.Text.Encoding.UTF8.GetString(configurator.Hosts.Read()).Split('\n')
        .Count(l => l.Contains(HostsEditor.Marker, StringComparison.Ordinal));
    Console.WriteLine(hosts == 0 ? "Dòng hosts của LunaDNS: không có" : $"Dòng hosts của LunaDNS: {hosts}");
    try
    {
        Console.WriteLine($"Journal: {new JsonFileJournalStore(new MacPaths().JournalFile).Load().Count} mục");
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("Journal: cần sudo để đọc");
    }
    return Ok;
}

static int Cleanup()
{
    if (!RequireRoot()) return BadArgs;
    var paths = new MacPaths();
    paths.EnsureCreated(DateTimeOffset.Now);
    using var held = Engine.TryLock(paths);
    if (held is null)
    {
        Console.Error.WriteLine("LunaDNS đang chạy (dịch vụ nền hoặc run); dùng 'lunadns off' hoặc Ctrl+C thay vì cleanup.");
        return Pending;
    }
    using var loggers = new LoggerFactory();
    loggers.AddProvider(new LunaDns.Core.Logging.RollingFileLoggerProvider(paths.LogDirectory));
    var configurator = MacNetworkConfigurator.CreateDefault(loggerFactory: loggers);
    var journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), LunaDns.Core.Platform.SystemChanges.Reverters(configurator));
    var result = journal.RollbackAll();
    PrintRollback(result);
    if (result.Success && result.Reverted == 0) Console.WriteLine("Không còn thay đổi nào cần hoàn tác.");
    return result.Success ? Ok : Pending;
}

static Task WaitForStopSignalAsync(out IDisposable registrations)
{
    var stop = new TaskCompletionSource();
    void Handler(PosixSignalContext c)
    {
        c.Cancel = true;
        stop.TrySetResult();
    }
    var list = new[]
    {
        PosixSignalRegistration.Create(PosixSignal.SIGINT, Handler),
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, Handler),
        PosixSignalRegistration.Create(PosixSignal.SIGHUP, Handler),
    };
    registrations = new Registrations(list);
    return stop.Task;
}

static async Task<int> RunAsync(bool proxy, bool allowProxyOverride)
{
    if (!RequireRoot()) return BadArgs;
    await using var engine = Engine.TryOpen(new MacPaths(), w => Console.Error.WriteLine("Cảnh báo: " + w));
    if (engine is null)
    {
        Console.Error.WriteLine("LunaDNS đã chạy (dịch vụ nền hoặc phiên run khác). Dùng 'lunadns on' nếu đã cài dịch vụ nền.");
        return Pending;
    }
    PrintRollback(engine.StartupRollback);
    if (!engine.StartupRollback.Success) return Pending;

    var commands = new DaemonCommands(engine.Features, engine.Journal, "run", engine.SettingsStore, engine.Loggers.CreateLogger<DaemonCommands>());
    var wanted = proxy ? new[] { FeatureKeys.DnsRouting, FeatureKeys.SniProxy } : [FeatureKeys.DnsRouting];
    foreach (var key in wanted)
    {
        var response = await commands.HandleAsync(new DaemonRequest(DaemonProtocol.Enable, key, allowProxyOverride), CancellationToken.None);
        if (response.Message is { } message) Console.Error.WriteLine(message);
        if (!response.Ok)
        {
            PrintRollback(await engine.Features.ShutdownAsync());
            return Pending;
        }
    }
    var status = engine.Features.GetStatus();
    Console.WriteLine($"Đang chạy. DNS stub 127.0.0.1:{engine.Configurator.Dns.Port}"
        + (status.ProxyPort is { } port ? $", proxy 127.0.0.1:{port}" : "") + ". Ctrl+C để tắt và khôi phục.");

    var stopped = WaitForStopSignalAsync(out var registrations);
    using (registrations) await stopped;

    var final = await engine.Features.ShutdownAsync();
    PrintRollback(final);
    Console.WriteLine(final.Success ? "Đã tắt và khôi phục." : "Còn thay đổi chưa hoàn tác; chạy: sudo lunadns cleanup");
    return final.Success ? Ok : Pending;
}

/// <summary>
/// Chạy dưới launchd bằng root. Lúc khởi động hoàn tác journal còn sót (thay tác vụ dọn dẹp lúc boot của Windows),
/// không tự bật tính năng nào (spec 4.7). SIGTERM (tắt máy, bootout) = tắt hết và hoàn tác.
/// </summary>
static async Task<int> DaemonAsync(string version)
{
    if (!RequireRoot()) return BadArgs;
    await using var engine = Engine.TryOpen(new MacPaths(), w => Console.Error.WriteLine("Cảnh báo: " + w));
    if (engine is null)
    {
        Console.Error.WriteLine("Đã có phiên LunaDNS khác giữ khoá (ví dụ 'sudo lunadns run').");
        return Pending;
    }
    PrintRollback(engine.StartupRollback);

    var commands = new DaemonCommands(engine.Features, engine.Journal, version, engine.SettingsStore, engine.Loggers.CreateLogger<DaemonCommands>());
    var consoleUser = new ConsoleUser(new ProcessRunner());
    var stopped = WaitForStopSignalAsync(out var registrations);
    using (registrations)
    {
        await using var server = DaemonServer.Start(DaemonProtocol.SocketPath, commands.HandleAsync, consoleUser.IsAllowed,
            engine.Loggers.CreateLogger<DaemonServer>());
        Console.WriteLine($"LunaDNS {version}: dịch vụ nền nghe {DaemonProtocol.SocketPath}.");
        await Task.WhenAny(stopped, server.Completion);
    }
    var final = await engine.Features.ShutdownAsync();
    PrintRollback(final);
    return final.Success ? Ok : Pending;
}

static async Task<int> InstallAsync()
{
    if (!RequireRoot()) return BadArgs;
    var installer = new Installer(new ProcessRunner(), Console.WriteLine);
    return await installer.InstallAsync(AppContext.BaseDirectory) ? Ok : Pending;
}

static async Task<int> UninstallAsync()
{
    if (!RequireRoot()) return BadArgs;
    new Installer(new ProcessRunner(), Console.WriteLine).Uninstall();
    var code = Cleanup();
    if (code != Ok)
    {
        Console.Error.WriteLine($"Giữ lại {MacPaths.DefaultRoot} vì còn thay đổi chưa hoàn tác.");
        return code;
    }
    if (Directory.Exists(MacPaths.DefaultRoot)) Directory.Delete(MacPaths.DefaultRoot, recursive: true);
    Console.WriteLine("Đã gỡ LunaDNS khỏi máy.");
    await Task.CompletedTask;
    return Ok;
}

internal sealed class Registrations(IDisposable[] items) : IDisposable
{
    public void Dispose()
    {
        foreach (var item in items) item.Dispose();
    }
}
