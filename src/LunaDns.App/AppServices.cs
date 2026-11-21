using LunaDns.App.Features;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
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
internal sealed partial class AppServices : IAsyncDisposable
{
    private readonly JsonFileSettingsStore _settingsStore;
    private readonly ProbeRunner _probes;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
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
        _logger = loggerFactory.CreateLogger<AppServices>();
        _settingsStore = new JsonFileSettingsStore(paths.SettingsFile);
        _settings = _settingsStore.Load();
        if (_settingsStore.DroppedDomains > 0) LogDroppedDomains(_logger, _settingsStore.DroppedDomains);
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

    public Task<DomainSelection?> GetDomainsAsync() => Features.GetDomainsAsync();

    /// <summary>Áp rồi mới lưu (spec KH8 3.1): settings.json chỉ ghi khi máy đã chạy theo danh sách mới.</summary>
    public async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var result = await Features.ApplyDomainsAsync(selection).ConfigureAwait(false);
        if (result.Success && Features.Availability == ControllerAvailability.Local)
            Settings = Settings with { Domains = selection };
        return result;
    }

    /// <summary>Lấy IP cho URL DoH tự nhập qua chuỗi DoH dựng sẵn (D2), không qua DNS hệ thống.</summary>
    public async Task<CustomDoh> ResolveCustomDohAsync(CustomDoh custom, CancellationToken cancellationToken)
    {
        using var doh = DohClient.Create(Settings.BootstrapDohServers(), LoggerFactory.CreateLogger<DohClient>());
        return await CustomDohBootstrap.ResolveAsync(custom, new DohHostResolver(doh), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Nút "Đo" (D2). Tự nhập phân giải lại trước; lỗi thì đo bằng IP cũ, máy đó có thể ra "lỗi".</summary>
    public async Task<IReadOnlyList<DohLatency>> MeasureDohAsync(CancellationToken cancellationToken)
    {
        if (Settings.CustomDoh is { } custom && CustomDohParser.TryParseEndpoint(custom.Endpoint, out _))
        {
            try
            {
                var resolved = await ResolveCustomDohAsync(custom, cancellationToken).ConfigureAwait(false);
                if (!ReferenceEquals(resolved, custom)) Settings = Settings with { CustomDoh = resolved };
            }
            catch (DohException)
            {
                // Giữ IP cũ; kết quả đo của Tự nhập sẽ cho người dùng thấy máy đó có dùng được không.
            }
        }
        return await DohLatencyMeter.Create(LoggerFactory).MeasureAsync(Settings.DohChoices(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProbeSummary> ProbeAsync(CancellationToken cancellationToken)
    {
        var selection = await ProbeSelectionAsync();
        var results = await _probes.RunAsync(selection, cancellationToken);
        LastProbe = results;
        return ProbeSummary.Create(results, _time.GetLocalNow(), h => ProbeTargets.GroupOf(h, selection));
    }

    /// <summary>Kiểm tra theo lựa chọn của nơi giữ thật (macOS: dịch vụ nền); hỏi không được thì dùng settings.json của app.</summary>
    internal async Task<DomainSelection> ProbeSelectionAsync() => await GetDomainsAsync() ?? Settings.Domains;

    public Task<IReadOnlyList<ProbeResult>> ProbeDomainsAsync(IReadOnlyList<string> hosts, CancellationToken cancellationToken) =>
        _probes.RunAsync(hosts, cancellationToken);

    public Task<int> ShutdownAsync() => Features.ShutdownAsync();

    public async ValueTask DisposeAsync()
    {
        await Features.DisposeAsync();
        LoggerFactory.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings.json có {Count} tên miền tuỳ chỉnh không hợp lệ, đã bỏ qua.")]
    private static partial void LogDroppedDomains(ILogger logger, int count);
}
