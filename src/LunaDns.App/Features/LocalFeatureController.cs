using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Probing;
using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.Features;

/// <summary>
/// Giao diện tự giữ FeatureSet và Journal (bản Windows, chạy bằng quyền Admin). Ctor hoàn tác Journal còn sót từ lần chạy trước
/// (spec 4.5); bên gọi phải giữ khoá một-bản-chạy trước. Hành vi y như AppServices trước Bước 3 của macOS.
/// </summary>
internal sealed partial class LocalFeatureController : IFeatureController
{
    private readonly ILogger _logger;
    private readonly Func<AppSettings> _settings;

    public LocalFeatureController(AppPaths paths, INetworkConfigurator configurator, Func<AppSettings> settings,
        ILoggerFactory loggerFactory, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        var clock = time ?? TimeProvider.System;
        _settings = settings;
        _logger = loggerFactory.CreateLogger<LocalFeatureController>();

        Journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile), SystemChanges.Reverters(configurator), clock);
        StartupRollback = Journal.RollbackAll();
        if (StartupRollback.Reverted > 0) LogStartupReverted(_logger, StartupRollback.Reverted);
        if (!StartupRollback.Success) LogStartupLeftovers(_logger, StartupRollback.Failed.Count);

        // Service dựng trước FeatureSet để Routed ban đầu đã có lớp 2; lambda đọc Features lúc chạy, sau khi ctor gán xong.
        DomainList = new DomainListService(new DomainListStore(paths.DomainListFile, DomainListKeys.Production), DomainListKeys.Production,
            settings().DomainListUpdates, () => settings().Domains, (domains, ct) => Features!.ReapplyAsync(domains, ct),
            clock, loggerFactory.CreateLogger<DomainListService>());
        var services = new CoreFeatureServices(() => settings().DohServers(), loggerFactory);
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            () => settings().Tuning(), clock, loggerFactory.CreateLogger<FeatureSet>(),
            routedDomains: FeatureDomains.Routed(settings().Domains, DomainList.Catalog));
    }

    public event EventHandler<FeatureStoppedEventArgs>? FeatureStopped
    {
        add => Features.FeatureStopped += value;
        remove => Features.FeatureStopped -= value;
    }

    public ChangeJournal Journal { get; }

    public FeatureSet Features { get; }

    /// <summary>Lớp 2 của danh sách tên miền (D4). Controller sở hữu, Dispose cùng controller.</summary>
    public DomainListService DomainList { get; }

    public ControllerAvailability Availability => ControllerAvailability.Local;

    public FeatureStatus Status => Features.GetStatus();

    public RollbackResult StartupRollback { get; }

    public int? JournalEntries => Journal.Entries.Count;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false) =>
        Features.EnableAsync(feature, allowProxyOverride);

    public Task<FeatureResult> DisableAsync(Feature feature) => Features.DisableAsync(feature);

    public Task<DisableAllResult> DisableAllAsync() => Features.DisableAllAsync();

    public Task<DomainSelection?> GetDomainsAsync() => Task.FromResult<DomainSelection?>(_settings().Domains);

    public async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var result = await DomainList.WithCatalogAsync(c => Features.ReapplyAsync(FeatureDomains.Routed(selection, c))).ConfigureAwait(false);
        if (result.Success) return new(true, null);
        var message = ErrorMessages.For(result.Error);
        return new(false, result.PreviousRestored ? message : $"{message} {Strings.ChangesLeftBehind}", result.Error);
    }

    public Task<DomainListState?> GetDomainListAsync() => Task.FromResult<DomainListState?>(DomainList.State);

    public Task<DomainListOfferResult> OfferDomainListAsync(byte[] file) => DomainList.OfferAsync(file);

    public Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled) => DomainList.SetEnabledAsync(enabled);

    public Task<TuningState?> GetTuningAsync()
    {
        var settings = _settings();
        return Task.FromResult<TuningState?>(new TuningState(settings.EffectiveDoh(), settings.CurrentCandidate()));
    }

    /// <summary>FeatureSet đọc tuning và DoH qua settings() lúc khởi động lại; AppServices đã lưu bản mới.</summary>
    public async Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate, Action? restore = null)
    {
        var result = await Features.ReapplyTuningAsync(restore).ConfigureAwait(false);
        if (result.Success) return new(true, null);
        var message = ErrorMessages.For(result.Error);
        return new(false, restore is null || result.PreviousRestored ? message : $"{message} {Strings.ChangesLeftBehind}", result.Error);
    }

    public async Task<int> ShutdownAsync() => (await Features.ShutdownAsync().ConfigureAwait(false)).Failed.Count;

    public async ValueTask DisposeAsync()
    {
        await Features.DisposeAsync().ConfigureAwait(false);
        DomainList.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã hoàn tác {Count} thay đổi còn sót từ lần chạy trước.")]
    private static partial void LogStartupReverted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Còn {Count} thay đổi từ lần chạy trước chưa hoàn tác được; giữ trong journal.")]
    private static partial void LogStartupLeftovers(ILogger logger, int count);
}
