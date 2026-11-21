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

    /// <summary>
    /// Nút "Đo" (D2). Tự nhập phân giải lại trước; lỗi thì đo bằng IP cũ, máy đó có thể ra "lỗi".
    /// Không ghi Settings (hàm chạy ở luồng nền): trả về CustomDoh đã phân giải lại để giao diện tự áp ở luồng UI.
    /// </summary>
    /// <param name="timeout">Thời gian tổng cho cả phân giải lại lẫn đo; hết giờ thì máy chưa xong ra "lỗi".</param>
    public async Task<DohMeasurement> MeasureDohAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var started = _time.GetTimestamp();
        var resolved = await RefreshCustomDohAsync(timeout, cancellationToken).ConfigureAwait(false);
        var remaining = timeout - _time.GetElapsedTime(started);
        var results = await DohLatencyMeter.Create(LoggerFactory)
            .MeasureAsync(DohCatalog.Choices(resolved ?? Settings.CustomDoh), remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, cancellationToken)
            .ConfigureAwait(false);
        return new DohMeasurement(results, resolved);
    }

    /// <summary>Phân giải lại host Tự nhập (D2). Lỗi hoặc không có Tự nhập thì null; người gọi dùng IP cũ.</summary>
    private async Task<CustomDoh?> RefreshCustomDohAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var custom = Settings.CustomDoh;
        if (custom is null || !CustomDohParser.TryParseEndpoint(custom.Endpoint, out _)) return null;
        using var resolveTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        resolveTimeout.CancelAfter(timeout);
        try
        {
            return await ResolveCustomDohAsync(custom, resolveTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DohException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return null; // giữ IP cũ; kết quả đo cho thấy máy đó có dùng được không
        }
    }

    /// <summary>Thời gian đo DoH trong một lượt dò D3 (cả lượt tối đa 60 giây, do giao diện giữ).</summary>
    internal static readonly TimeSpan TuneDohTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// "Tìm cấu hình hợp mạng" (D3) trên 4 host Steam. Không ghi Settings (chạy ở luồng nền): trả TuneRun để giao diện áp ở
    /// luồng UI. Tổ hợp "đang dùng" lấy từ nơi giữ thật (macOS: daemon); hỏi không được thì dùng mặc định.
    /// </summary>
    public async Task<TuneRun> TuneAsync(IProgress<TuneProgress>? progress, CancellationToken cancellationToken)
    {
        var current = await Features.GetTuningAsync().ConfigureAwait(false);
        var local = Features.Availability == ControllerAvailability.Local;
        // Phân giải lại Tự nhập và đo chung một hạn 10 giây (như MeasureDohAsync).
        var started = _time.GetTimestamp();
        var resolved = local ? await RefreshCustomDohAsync(TuneDohTimeout, cancellationToken).ConfigureAwait(false) : null;
        var choices = TuneChoices(Features.Availability, resolved ?? Settings.CustomDoh);
        var meter = DohLatencyMeter.Create(LoggerFactory);
        var clients = new List<DohClient>();
        try
        {
            var tuner = new NetworkTuner(
                (c, token) =>
                {
                    var remaining = TuneDohTimeout - _time.GetElapsedTime(started);
                    return meter.MeasureAsync(c, remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, token);
                },
                new SystemHostResolver(LoggerFactory.CreateLogger<SystemHostResolver>()),
                server =>
                {
                    var client = DohClient.Create([server], LoggerFactory.CreateLogger<DohClient>());
                    lock (clients) clients.Add(client);
                    return new DohHostResolver(client);
                },
                candidate => new TlsProbe(candidate.Split, candidate.Delay, LoggerFactory.CreateLogger<TlsProbe>()),
                loggers: LoggerFactory);
            var result = await tuner
                .RunAsync(ProbeTargets.Steam, choices, current?.Candidate ?? TuneCandidate.Default, progress, cancellationToken)
                .ConfigureAwait(false);
            ProbeSummary? summary = null;
            if (result.Probe.Count > 0)
            {
                LastProbe = result.Probe;
                summary = ProbeSummary.Create(result.Probe, _time.GetLocalNow(), _ => DomainGroup.Steam);
            }
            return new TuneRun(result, current, resolved, summary);
        }
        finally
        {
            foreach (var client in clients) client.Dispose();
        }
    }

    /// <summary>Máy DoH để dò: macOS chỉ 4 máy dựng sẵn, vì daemon không nhận Tự nhập (spec D3).</summary>
    internal static IReadOnlyList<DohChoice> TuneChoices(ControllerAvailability availability, CustomDoh? custom) =>
        DohCatalog.Choices(availability == ControllerAvailability.Local ? custom : null);

    /// <summary>
    /// Windows: lưu trước (FeatureSet đọc settings lúc khởi động lại); lỗi thì FeatureSet đặt lại bản cũ và bật lại đúng các
    /// tính năng đang chạy. macOS: daemon tự áp và lưu; xong mới chép vào settings.json của app để Kiểm tra dùng cùng cấu hình.
    /// Không ghi được settings.json thì báo lỗi thay vì ném.
    /// </summary>
    public async Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (Features.Availability != ControllerAvailability.Local)
        {
            var remote = await Features.ApplyTuningAsync(doh, candidate).ConfigureAwait(false);
            if (!remote.Success) return remote;
            var copy = Settings.WithTuning(doh, candidate);
            if (!TrySaveSettings(copy, out _)) _settings = copy; // daemon đã áp và lưu; bản chép của app chỉ giữ trong bộ nhớ
            return remote;
        }
        var previous = Settings;
        if (!TrySaveSettings(previous.WithTuning(doh, candidate), out var saveError))
            return new TuningApplyResult(false, saveError); // chưa đổi gì
        string? restoreError = null;
        var result = await Features.ApplyTuningAsync(doh, candidate, () =>
        {
            // Ghi file lỗi thì vẫn đặt bản cũ trong bộ nhớ để lần chạy lại đọc đúng cấu hình cũ.
            if (!TrySaveSettings(previous, out restoreError)) _settings = previous;
        }).ConfigureAwait(false);
        return result.Success || restoreError is null ? result : result with { Message = $"{result.Message} {restoreError}" };
    }

    /// <summary>Lưu settings.json; lỗi ghi file thì trả lời cho người dùng thay vì ném (Settings giữ nguyên).</summary>
    private bool TrySaveSettings(AppSettings settings, out string? error)
    {
        try
        {
            Settings = settings;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSettingsSaveFailed(_logger, ex);
            error = Strings.Format(nameof(Strings.SettingsSaveFailedFormat), ex.Message);
            return false;
        }
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Không ghi được settings.json khi áp cấu hình dò được.")]
    private static partial void LogSettingsSaveFailed(ILogger logger, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "settings.json có {Count} tên miền tuỳ chỉnh không hợp lệ, đã bỏ qua.")]
    private static partial void LogDroppedDomains(ILogger logger, int count);
}

/// <summary>Kết quả nút "Đo": số đo từng máy và CustomDoh vừa phân giải lại (null nếu không có/không phân giải được).</summary>
public sealed record DohMeasurement(IReadOnlyList<DohLatency> Results, CustomDoh? Resolved);

/// <param name="Current">Cấu hình trước khi dò; null nếu không hỏi được nơi giữ thật.</param>
/// <param name="ResolvedCustom">Tự nhập đã phân giải lại, để giao diện lưu ở luồng UI (như nút Đo).</param>
/// <param name="Summary">Kết quả phân loại nhóm Steam cho khối Kiểm tra; null khi không máy DoH nào trả lời.</param>
public sealed record TuneRun(TuneResult Result, TuningState? Current, CustomDoh? ResolvedCustom, ProbeSummary? Summary);
