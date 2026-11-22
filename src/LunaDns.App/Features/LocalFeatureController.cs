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
/// (spec 4.5) rồi quét dấu vết LunaDNS mồ côi (B5 + F7); bên gọi phải giữ khoá một-bản-chạy trước. Hành vi y như AppServices trước Bước 3 của macOS.
/// </summary>
internal sealed partial class LocalFeatureController : IFeatureController
{
    private readonly ILogger _logger;
    private readonly Func<AppSettings> _settings;
    private readonly Action<AppSettings> _saveSettings;

    /// <param name="saveSettings">Lưu settings.json; gọi trong cổng của DomainListService, sau khi áp xong (áp trước, lưu sau).</param>
    public LocalFeatureController(AppPaths paths, INetworkConfigurator configurator, Func<AppSettings> settings,
        ILoggerFactory loggerFactory, TimeProvider? time = null, Action<AppSettings>? saveSettings = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(configurator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        var clock = time ?? TimeProvider.System;
        _settings = settings;
        _saveSettings = saveSettings ?? (_ => { });
        _logger = loggerFactory.CreateLogger<LocalFeatureController>();

        Journal = new ChangeJournal(new JsonFileJournalStore(paths.JournalFile, loggerFactory.CreateLogger<JsonFileJournalStore>()),
            SystemChanges.Reverters(configurator), clock);
        StartupRollback = Journal.RollbackAll();
        if (StartupRollback.Reverted > 0) LogStartupReverted(_logger, StartupRollback.Reverted);
        if (!StartupRollback.Success) LogStartupLeftovers(_logger, StartupRollback.Failed.Count);
        // B5 + F7: vẫn giữ mutex, chưa tính năng nào chạy; phải trước ResumeAsync (F1) để không gỡ thứ sắp tự bật lại.
        OrphanSweeper.Sweep(Journal, configurator, _logger);

        // Service dựng trước FeatureSet để Routed ban đầu đã có lớp 2; lambda đọc Features lúc chạy, sau khi ctor gán xong.
        DomainList = new DomainListService(new DomainListStore(paths.DomainListFile, DomainListKeys.Production), DomainListKeys.Production,
            settings().DomainListUpdates, () => settings().Domains, (domains, ct) => Features!.ReapplyAsync(domains, ct),
            clock, loggerFactory.CreateLogger<DomainListService>());
        var services = new CoreFeatureServices(() => settings().DohServers(), loggerFactory);
        Features = new FeatureSet(Journal, new SystemChanges(Journal, configurator), services,
            () => settings().Tuning(), clock, loggerFactory.CreateLogger<FeatureSet>(),
            routedDomains: FeatureDomains.Routed(settings().Domains, DomainList.Catalog));
        Resumer = new FeatureResumer(Features, settings, _saveSettings, time: clock, logger: loggerFactory.CreateLogger<FeatureResumer>());
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

    /// <summary>F1: wanted và công tắc nằm trong settings.json của app; tự bật lúc mở app.</summary>
    public FeatureResumer Resumer { get; }

    public ResumeState? Resume => Resumer.State;

    public ControllerAvailability Availability => ControllerAvailability.Local;

    public FeatureStatus Status => Features.GetStatus();

    public RollbackResult StartupRollback { get; }

    public int? JournalEntries => Journal.Entries.Count;

    public bool ServiceOutdated => false;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public async Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false)
    {
        var result = await Features.EnableAsync(feature, allowProxyOverride).ConfigureAwait(false);
        if (result.Outcome is FeatureOutcome.Enabled or FeatureOutcome.Unchanged) Resumer.Record(feature, true);
        return result;
    }

    public async Task<FeatureResult> DisableAsync(Feature feature)
    {
        var result = await Features.DisableAsync(feature).ConfigureAwait(false);
        if (result.Outcome is FeatureOutcome.Disabled or FeatureOutcome.Unchanged) Resumer.Record(feature, false);
        return result;
    }

    public async Task<DisableAllResult> DisableAllAsync()
    {
        var result = await Features.DisableAllAsync().ConfigureAwait(false);
        Resumer.Forget();
        return result;
    }

    /// <summary>Sau khi ctor hoàn tác journal: hoàn tác không sạch thì không tự bật.</summary>
    public Task ResumeAsync() => Resumer.RunAsync(StartupRollback.Success);

    public Task<string?> SetAutoResumeAsync(bool enabled) =>
        Task.FromResult(Resumer.SetEnabled(enabled) ? null : Strings.AutoResumeSaveFailed);

    public Task<DomainSelection?> GetDomainsAsync() => Task.FromResult<DomainSelection?>(_settings().Domains);

    public async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        // Lưu trong cùng cổng: lượt Offer hay bật/tắt lớp 2 đọc settings().Domains sẽ thấy đúng danh sách vừa áp.
        var result = await DomainList.WithCatalogAsync(async c =>
        {
            var applied = await Features.ReapplyAsync(FeatureDomains.Routed(selection, c)).ConfigureAwait(false);
            if (applied.Success) _saveSettings(_settings() with { Domains = selection });
            return applied;
        }).ConfigureAwait(false);
        if (result.Success) return new(true, null);
        var message = ErrorMessages.For(result.Error);
        return new(false, result.PreviousRestored ? message : $"{message} {Strings.ChangesLeftBehind}", result.Error);
    }

    public Task<DomainListState?> GetDomainListAsync() => Task.FromResult<DomainListState?>(DomainList.State);

    public Task<DomainListOfferResult> OfferDomainListAsync(byte[] file) => DomainList.OfferAsync(file);

    public Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled) =>
        DomainList.SetEnabledAsync(enabled, () => _saveSettings(_settings() with { DomainListUpdates = enabled }));

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

    public async Task<int> ShutdownAsync(ShutdownReason reason)
    {
        if (reason == ShutdownReason.UserQuit) Resumer.Forget();
        return (await Features.ShutdownAsync().ConfigureAwait(false)).Failed.Count;
    }

    public ValueTask DisposeAsync() => Features.DisposeAsync();

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã hoàn tác {Count} thay đổi còn sót từ lần chạy trước.")]
    private static partial void LogStartupReverted(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Còn {Count} thay đổi từ lần chạy trước chưa hoàn tác được; giữ trong journal.")]
    private static partial void LogStartupLeftovers(ILogger logger, int count);
}
