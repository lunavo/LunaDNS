using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Logging;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using LunaDns.Platform.Windows;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// Composition root của giao diện. Ctor hoàn tác Journal còn sót từ lần chạy trước (spec 4.5); bên gọi phải giữ mutex trước.
/// DisposeAsync = FeatureSet.ShutdownAsync, tức là hoàn tác toàn bộ: KHÔNG dựng lớp này cho --report hay --cleanup.
/// </summary>
internal sealed partial class AppServices : IAsyncDisposable
{
    private readonly JsonFileSettingsStore _settingsStore;
    private readonly ProbeRunner _probes;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private volatile AppSettings _settings;

    public AppServices(AppPaths paths, INetworkConfigurator configurator, ILoggerFactory loggerFactory, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        paths.EnsureCreated();
        Paths = paths;
        LoggerFactory = loggerFactory;
        _time = time ?? TimeProvider.System;
        _logger = loggerFactory.CreateLogger<AppServices>();
        _settingsStore = new JsonFileSettingsStore(paths.SettingsFile);
        _settings = _settingsStore.Load();

        Journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator), _time);
        StartupRollback = Journal.RollbackAll();
        if (StartupRollback.Reverted > 0) LogStartupReverted(_logger, StartupRollback.Reverted);
        if (!StartupRollback.Success) LogStartupLeftovers(_logger, StartupRollback.Failed.Count);

        var services = new CoreFeatureServices(() => Settings.DohServers(), loggerFactory);
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            () => Settings.Tuning(), _time, loggerFactory.CreateLogger<FeatureSet>());
        _probes = new ProbeRunner(() => Settings, loggerFactory);
    }

    public AppPaths Paths { get; }

    public ILoggerFactory LoggerFactory { get; }

    public ChangeJournal Journal { get; }

    public FeatureSet Features { get; }

    public RollbackResult StartupRollback { get; }

    /// <summary>Gán là lưu settings.json ngay. Watchdog và DoH đọc giá trị mới ở lần khởi động dịch vụ kế tiếp.</summary>
    public AppSettings Settings
    {
        get => _settings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _settingsStore.Save(value);
            _settings = value;
        }
    }

    /// <summary>Kết quả "Kiểm tra" gần nhất, dùng cho nút "Chẩn đoán" (KH5 quyết định 15).</summary>
    public IReadOnlyList<ProbeResult>? LastProbe { get; private set; }

    public static AppServices CreateDefault(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureCreated();
        var loggerFactory = CreateLoggerFactory(paths);
        return new AppServices(paths, WindowsNetworkConfigurator.CreateDefault(loggerFactory), loggerFactory);
    }

    /// <summary>
    /// Log file xoay vòng ở %ProgramData%\LunaDNS\logs (spec 6). Dùng LoggerFactory.AddProvider (không dùng LoggerFactory.Create)
    /// để factory sở hữu provider và đóng file khi Dispose. --cleanup và --report (T7) dùng chung hàm này.
    /// </summary>
    public static ILoggerFactory CreateLoggerFactory(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var factory = new LoggerFactory();
        factory.AddProvider(new RollingFileLoggerProvider(paths.LogDirectory));
        return factory;
    }

    public async Task<ProbeSummary> ProbeAsync(CancellationToken cancellationToken)
    {
        var results = await _probes.RunAsync(cancellationToken);
        LastProbe = results;
        return ProbeSummary.Create(results, _time.GetLocalNow());
    }

    public Task<RollbackResult> ShutdownAsync() => Features.ShutdownAsync();

    public async ValueTask DisposeAsync()
    {
        await Features.DisposeAsync();
        LoggerFactory.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã hoàn tác {Count} thay đổi còn sót từ lần chạy trước.")]
    private static partial void LogStartupReverted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Còn {Count} thay đổi từ lần chạy trước chưa hoàn tác được; giữ trong journal.")]
    private static partial void LogStartupLeftovers(ILogger logger, int count);
}
