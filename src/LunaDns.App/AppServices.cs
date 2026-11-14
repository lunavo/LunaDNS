using LunaDns.App.Features;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Logging;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App;

/// <summary>
/// Composition root của giao diện. Tính năng nằm sau <see cref="IFeatureController"/>: Windows tự giữ FeatureSet
/// (LocalFeatureController, ctor hoàn tác Journal còn sót; bên gọi phải giữ khoá một-bản-chạy trước), macOS gửi lệnh tới dịch vụ nền.
/// DisposeAsync của bản Windows hoàn tác toàn bộ: KHÔNG dựng lớp này cho --report hay --cleanup.
/// </summary>
internal sealed class AppServices : IAsyncDisposable
{
    private readonly JsonFileSettingsStore _settingsStore;
    private readonly ProbeRunner _probes;
    private readonly TimeProvider _time;
    private volatile AppSettings _settings;

    public AppServices(AppPaths paths, Func<AppServices, IFeatureController> features, ILoggerFactory loggerFactory, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        paths.EnsureCreated();
        Paths = paths;
        LoggerFactory = loggerFactory;
        _time = time ?? TimeProvider.System;
        _settingsStore = new JsonFileSettingsStore(paths.SettingsFile);
        _settings = _settingsStore.Load();
        Features = features(this);
        _probes = new ProbeRunner(() => Settings, loggerFactory);
    }

    /// <summary>Bản tại chỗ (Windows, và test): FeatureSet đọc settings hiện tại của chính AppServices này.</summary>
    public AppServices(AppPaths paths, INetworkConfigurator configurator, ILoggerFactory loggerFactory, TimeProvider? time = null)
        : this(paths, s => new LocalFeatureController(paths, configurator, () => s.Settings, loggerFactory, time), loggerFactory, time)
    {
    }

    public AppPaths Paths { get; }

    public ILoggerFactory LoggerFactory { get; }

    public IFeatureController Features { get; }

    public RollbackResult StartupRollback => Features.StartupRollback;

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

    /// <summary>
    /// Log file xoay vòng trong thư mục log của AppPaths (spec 6). Dùng LoggerFactory.AddProvider (không dùng LoggerFactory.Create)
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

    public Task<int> ShutdownAsync() => Features.ShutdownAsync();

    public async ValueTask DisposeAsync()
    {
        await Features.DisposeAsync();
        LoggerFactory.Dispose();
    }
}
