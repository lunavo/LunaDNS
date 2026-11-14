using System.Net;
using System.Runtime.InteropServices;
using LunaDns.Core.Features;
using LunaDns.Core.Hosts;
using LunaDns.Core.Journal;
using LunaDns.Core.Logging;
using LunaDns.Core.Platform;
using LunaDns.Core.Settings;
using LunaDns.MacCli;
using LunaDns.Platform.MacOS;
using Microsoft.Extensions.Logging;

// CLI tạm cho macOS (bước 1). Bước 2 sẽ chuyển phần "run" thành launchd daemon, giao diện nói chuyện qua socket.
const int Ok = 0, Pending = 1, BadArgs = 2;

var command = args.Length > 0 ? args[0] : "help";
var options = args.Skip(1).ToHashSet(StringComparer.Ordinal);
return command switch
{
    "run" => await RunAsync(options.Contains("--proxy"), options.Contains("--allow-proxy-override")),
    "cleanup" => Cleanup(),
    "status" => Status(),
    "help" or "--help" or "-h" => Help(Ok),
    _ => Help(BadArgs),
};

static int Help(int code)
{
    Console.WriteLine("""
        LunaDNS cho macOS (bản dòng lệnh thử nghiệm)

          sudo lunadns run [--proxy] [--allow-proxy-override]
              Bật Định tuyến DNS (stub DoH 127.0.0.1:15353 + /etc/resolver). --proxy bật thêm Proxy SNI (PAC hệ thống).
              Giữ cửa sổ mở; Ctrl+C để tắt và khôi phục.
          sudo lunadns cleanup   Hoàn tác mọi thay đổi còn trong journal (khi lần chạy trước chết giữa chừng).
          lunadns status         Xem LunaDNS đang đổi gì trên máy.

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

static (MacPaths Paths, ILoggerFactory Loggers) Open()
{
    var paths = new MacPaths();
    var quarantined = paths.EnsureCreated(DateTimeOffset.Now);
    var loggers = new LoggerFactory();
    loggers.AddProvider(new RollingFileLoggerProvider(paths.LogDirectory));
    if (quarantined is not null)
        Console.Error.WriteLine($"Cảnh báo: thư mục dữ liệu cũ cho phép người khác ghi, đã cách ly sang {quarantined}.");
    return (paths, loggers);
}

/// <summary>Khoá độc quyền (flock) trên file khoá: thay mutex Global\LunaDNS. Null nếu phiên khác đang giữ.</summary>
static FileStream? TryLock(MacPaths paths)
{
    try
    {
        return new FileStream(paths.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    catch (IOException)
    {
        return null;
    }
}

static ChangeJournal NewJournal(MacPaths paths, INetworkConfigurator configurator) =>
    new(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator));

static void PrintRollback(RollbackResult result)
{
    if (result.Reverted > 0) Console.WriteLine($"Đã hoàn tác {result.Reverted} thay đổi.");
    foreach (var failure in result.Failed)
        Console.Error.WriteLine($"Chưa hoàn tác được {failure.Entry.Op} ({failure.Entry.Feature}): {failure.Error.Message}");
}

static int Cleanup()
{
    if (!RequireRoot()) return BadArgs;
    var (paths, loggers) = Open();
    using (loggers)
    {
        using var held = TryLock(paths);
        if (held is null)
        {
            Console.Error.WriteLine("LunaDNS đang chạy ở phiên khác; tắt phiên đó (Ctrl+C) thay vì cleanup.");
            return Pending;
        }
        var configurator = MacNetworkConfigurator.CreateDefault(loggerFactory: loggers);
        var result = NewJournal(paths, configurator).RollbackAll();
        PrintRollback(result);
        if (result.Success && result.Reverted == 0) Console.WriteLine("Không còn thay đổi nào cần hoàn tác.");
        return result.Success ? Ok : Pending;
    }
}

static int Status()
{
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
    catch (ConfiguratorException ex)
    {
        Console.WriteLine($"Proxy SNI: không đọc được ({ex.Message})");
    }

    var hosts = System.Text.Encoding.UTF8.GetString(configurator.Hosts.Read()).Split('\n')
        .Count(l => l.Contains(HostsEditor.Marker, StringComparison.Ordinal));
    Console.WriteLine(hosts == 0 ? "Dòng hosts của LunaDNS: không có" : $"Dòng hosts của LunaDNS: {hosts}");

    var paths = new MacPaths();
    try
    {
        var entries = new JsonFileJournalStore(paths.JournalFile).Load();
        Console.WriteLine($"Journal: {entries.Count} mục");
    }
    catch (UnauthorizedAccessException)
    {
        Console.WriteLine("Journal: cần sudo để đọc");
    }
    return Ok;
}

static async Task<int> RunAsync(bool proxy, bool allowProxyOverride)
{
    if (!RequireRoot()) return BadArgs;
    var (paths, loggers) = Open();
    using (loggers)
    {
        using var held = TryLock(paths);
        if (held is null)
        {
            Console.Error.WriteLine("LunaDNS đã chạy ở phiên khác.");
            return Pending;
        }

        var configurator = MacNetworkConfigurator.CreateDefault(loggerFactory: loggers);
        var journal = NewJournal(paths, configurator);
        var leftovers = journal.RollbackAll();
        PrintRollback(leftovers);
        if (!leftovers.Success) return Pending;

        var settings = new AppSettings();
        var services = new CoreFeatureServices(() => settings.DohServers(), loggers,
            dnsEndPoint: new IPEndPoint(IPAddress.Loopback, configurator.Dns.Port));
        await using var features = new FeatureSet(journal, new SystemChanges(journal, configurator), services,
            settings.Tuning, logger: loggers.CreateLogger<FeatureSet>());
        features.FeatureStopped += (_, e) => Console.Error.WriteLine(
            $"Tính năng {e.Feature} đã dừng bất ngờ{(e.ChangesLeftBehind ? "; còn thay đổi chưa hoàn tác, chạy: sudo lunadns cleanup" : "")}.");

        var wanted = proxy ? new[] { Feature.DnsRouting, Feature.SniProxy } : [Feature.DnsRouting];
        foreach (var feature in wanted)
        {
            var result = await features.EnableAsync(feature, allowProxyOverride);
            Console.WriteLine($"{feature}: {result.Outcome}{(result.Error == FeatureError.None ? "" : $" ({result.Error})")}");
            if (result.Outcome == FeatureOutcome.Failed)
            {
                var shutdown = await features.ShutdownAsync();
                PrintRollback(shutdown);
                return Pending;
            }
        }

        var status = features.GetStatus();
        Console.WriteLine($"Đang chạy. DNS stub 127.0.0.1:{configurator.Dns.Port}"
            + (status.ProxyPort is { } port ? $", proxy 127.0.0.1:{port}" : "") + ". Ctrl+C để tắt và khôi phục.");

        var stop = new TaskCompletionSource();
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, c => { c.Cancel = true; stop.TrySetResult(); });
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; stop.TrySetResult(); });
        using var sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, c => { c.Cancel = true; stop.TrySetResult(); });
        await stop.Task;

        var final = await features.ShutdownAsync();
        PrintRollback(final);
        Console.WriteLine(final.Success ? "Đã tắt và khôi phục." : "Còn thay đổi chưa hoàn tác; chạy: sudo lunadns cleanup");
        return final.Success ? Ok : Pending;
    }
}
