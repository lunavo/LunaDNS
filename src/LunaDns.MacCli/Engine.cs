using System.Net;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Logging;
using LunaDns.Core.Platform;
using LunaDns.Core.Settings;
using LunaDns.Platform.MacOS;
using Microsoft.Extensions.Logging;

namespace LunaDns.MacCli;

/// <summary>
/// Phần chạy bằng root, dùng chung cho <c>run</c> (tiền cảnh) và <c>daemon</c> (launchd): thư mục dữ liệu, khoá độc quyền,
/// hoàn tác journal còn sót, FeatureSet. Dispose = tắt mọi tính năng và hoàn tác.
/// </summary>
internal sealed class Engine : IAsyncDisposable
{
    private readonly FileStream _lock;

    private Engine(MacPaths paths, FileStream held, ILoggerFactory loggers, MacNetworkConfigurator configurator)
    {
        Paths = paths;
        _lock = held;
        Loggers = loggers;
        Configurator = configurator;
        Journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator));
        StartupRollback = Journal.RollbackAll();
        var settings = new AppSettings();
        var services = new CoreFeatureServices(() => settings.DohServers(), loggers,
            dnsEndPoint: new IPEndPoint(IPAddress.Loopback, configurator.Dns.Port));
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            settings.Tuning, logger: loggers.CreateLogger<FeatureSet>());
    }

    public MacPaths Paths { get; }

    public ILoggerFactory Loggers { get; }

    public MacNetworkConfigurator Configurator { get; }

    public ChangeJournal Journal { get; }

    public FeatureSet Features { get; }

    public RollbackResult StartupRollback { get; }

    /// <summary>Null nếu đã có phiên khác (run hoặc daemon) giữ khoá.</summary>
    public static Engine? TryOpen(MacPaths paths, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(warn);
        var quarantined = paths.EnsureCreated(DateTimeOffset.Now);
        if (quarantined is not null) warn($"Thư mục dữ liệu cũ cho phép người khác ghi, đã cách ly sang {quarantined}.");
        var held = TryLock(paths);
        if (held is null) return null;
        var loggers = new LoggerFactory();
        loggers.AddProvider(new RollingFileLoggerProvider(paths.LogDirectory));
        return new Engine(paths, held, loggers, MacNetworkConfigurator.CreateDefault(loggerFactory: loggers));
    }

    /// <summary>Khoá flock trên file khoá, thay mutex Global\LunaDNS của Windows.</summary>
    public static FileStream? TryLock(MacPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            return new FileStream(paths.LockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Features.DisposeAsync().ConfigureAwait(false);
        Loggers.Dispose();
        await _lock.DisposeAsync().ConfigureAwait(false);
    }
}
