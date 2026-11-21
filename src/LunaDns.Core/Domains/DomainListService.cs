using LunaDns.Core.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Domains;

/// <summary>
/// Chỗ chạy chung của lớp 2 (spec D4): Windows trong tiến trình app, macOS trong daemon. Xác minh, gộp, reapply khi
/// Routed đổi, rồi mới ghi file. Mọi thao tác chạy tuần tự qua một cổng; set-domains/Lưu Tuỳ chỉnh dùng WithCatalogAsync
/// để Routed tính bằng catalog đúng lúc áp. Công tắc lưu ở settings.json do bên gọi giữ; service chỉ giữ giá trị đang dùng.
/// </summary>
public sealed partial class DomainListService : IDisposable
{
    private readonly DomainListStore _store;
    private readonly IReadOnlyDictionary<string, string> _keys;
    private readonly Func<DomainSelection> _selection;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<ReapplyResult>> _reapply;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile DomainListStoreData _data;
    private volatile bool _enabled;

    public DomainListService(DomainListStore store, IReadOnlyDictionary<string, string> keys, bool enabled,
        Func<DomainSelection> selection, Func<IReadOnlyList<string>, CancellationToken, Task<ReapplyResult>> reapply,
        TimeProvider? time = null, ILogger? logger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _reapply = reapply ?? throw new ArgumentNullException(nameof(reapply));
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        _enabled = enabled;
        _data = store.Load();
    }

    public EffectiveCatalog Catalog => CatalogFor(_data, _enabled);

    public DomainListState State
    {
        get
        {
            var data = _data;
            var catalog = CatalogFor(data, _enabled);
            return new DomainListState(_enabled, data.Current?.Serial ?? 0, data.Current?.Issued, data.CheckedAt, catalog.AllAdded);
        }
    }

    public async Task<DomainListOfferResult> OfferAsync(byte[] file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_enabled) return new(DomainListOutcome.Disabled);
            var verified = DomainListVerifier.Verify(file, _keys, _data.HighestSerial);
            var now = _time.GetUtcNow();
            if (verified.Outcome == DomainListOutcome.NotNewer)
            {
                TrySave(_data with { CheckedAt = now });
                return new(DomainListOutcome.NotNewer);
            }
            if (verified.Outcome != DomainListOutcome.Accepted)
            {
                LogRejected(_logger, verified.Outcome);
                return new(verified.Outcome);
            }
            if (verified.Dropped > 0) LogDropped(_logger, verified.Dropped, verified.Update!.Serial);

            var next = new DomainListStoreData(verified.Update, verified.Update!.Serial, now);
            var before = CatalogFor(_data, true);
            var after = CatalogFor(next, true);
            var applied = await ApplyAsync(before, after, cancellationToken).ConfigureAwait(false);
            if (!applied.Success) return Failed(applied);
            try
            {
                _store.Save(next);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogSaveFailed(_logger, ex);
                var back = await ApplyAsync(after, before, CancellationToken.None).ConfigureAwait(false);
                return new(DomainListOutcome.SaveFailed, PreviousRestored: back.Success);
            }
            _data = next;
            return new(DomainListOutcome.Accepted, CountAdded(before, after), verified.Dropped);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DomainListOfferResult> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (enabled == _enabled) return new(DomainListOutcome.Accepted);
            var before = CatalogFor(_data, _enabled);
            var after = CatalogFor(_data, enabled);
            var applied = await ApplyAsync(before, after, cancellationToken).ConfigureAwait(false);
            if (!applied.Success) return Failed(applied);
            _enabled = enabled;
            return new(DomainListOutcome.Accepted, CountAdded(before, after));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> WithCatalogAsync<T>(Func<EffectiveCatalog, Task<T>> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(Catalog).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private static EffectiveCatalog CatalogFor(DomainListStoreData data, bool enabled) =>
        enabled && data.Current is { } current ? EffectiveCatalog.From(current.Groups, current.Issued) : EffectiveCatalog.Builtin;

    private async Task<ReapplyResult> ApplyAsync(EffectiveCatalog before, EffectiveCatalog after, CancellationToken cancellationToken)
    {
        var selection = _selection();
        var routed = FeatureDomains.Routed(selection, after);
        if (routed.SequenceEqual(FeatureDomains.Routed(selection, before))) return new ReapplyResult(true, false);
        return await _reapply(routed, cancellationToken).ConfigureAwait(false);
    }

    private static int CountAdded(EffectiveCatalog before, EffectiveCatalog after) =>
        DomainCatalog.Selectable.Sum(g => after.Added(g).Except(before.Added(g), StringComparer.Ordinal).Count());

    private DomainListOfferResult Failed(ReapplyResult result)
    {
        LogReapplyFailed(_logger, result.Error);
        return new(DomainListOutcome.ReapplyFailed, Error: result.Error, PreviousRestored: result.PreviousRestored);
    }

    private void TrySave(DomainListStoreData data)
    {
        try
        {
            _store.Save(data);
            _data = data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bỏ danh sách tên miền tải về: {Outcome}.")]
    private static partial void LogRejected(ILogger logger, DomainListOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Danh sách tên miền bản {Serial}: bỏ {Count} dòng không hợp lệ.")]
    private static partial void LogDropped(ILogger logger, int count, long serial);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không áp được danh sách tên miền mới: {Error}.")]
    private static partial void LogReapplyFailed(ILogger logger, FeatureError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không ghi được domain-list.json.")]
    private static partial void LogSaveFailed(ILogger logger, Exception ex);
}
