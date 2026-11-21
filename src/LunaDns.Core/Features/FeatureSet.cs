using LunaDns.Core.Domains;
using System.Collections.Concurrent;
using System.Net;
using LunaDns.Core.Dns;
using LunaDns.Core.Journal;
using LunaDns.Core.Platform;
using LunaDns.Core.Proxy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Features;

/// <summary>
/// Bật/tắt từng tính năng (spec 5, 6).
/// Bật: mở dịch vụ nền (stub, proxy, listener) → SystemChanges (ghi Journal rồi đổi hệ thống). Lỗi giữa chừng thì
/// hoàn tác phần của chính tính năng đó. Dịch vụ không mở được thì chưa ghi gì.
/// Tắt: hoàn tác Journal trước, dừng dịch vụ sau, để hệ thống không trỏ vào dịch vụ đã chết.
/// Watchdog: dịch vụ chết thì mở lại (proxy giữ đúng cổng), tối đa 3 lần/phút; quá thì tắt tính năng và báo FeatureStopped.
/// Mọi lệnh và watchdog chạy tuần tự qua một cổng.
/// </summary>
public sealed partial class FeatureSet : IAsyncDisposable
{
    private static readonly Feature[] ShutdownOrder = [Feature.PubgMode, Feature.SniProxy, Feature.DnsRouting];
    private static readonly Feature[] RoutedFeatures = [Feature.DnsRouting, Feature.SniProxy];

    private readonly ChangeJournal _journal;
    private readonly SystemChanges _changes;
    private readonly IFeatureServices _services;
    private readonly Func<FeatureTuning> _tuning;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<Feature, Running> _running = new();
    private volatile IReadOnlyList<string> _routed;

    public FeatureSet(
        ChangeJournal journal,
        SystemChanges changes,
        IFeatureServices services,
        Func<FeatureTuning>? tuning = null,
        TimeProvider? time = null,
        ILogger<FeatureSet>? logger = null,
        IReadOnlyList<string>? routedDomains = null)
    {
        _journal = journal;
        _changes = changes;
        _services = services;
        _tuning = tuning ?? (() => FeatureTuning.Default);
        _time = time ?? TimeProvider.System;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _routed = routedDomains ?? FeatureDomains.Routed(new DomainSelection());
    }

    /// <summary>Danh sách tên miền DNS/SNI đang dùng (spec KH8 3.1).</summary>
    public IReadOnlyList<string> RoutedDomains => _routed;

    /// <summary>Watchdog đã tắt một tính năng. Gọi từ luồng nền; giao diện tự chuyển về luồng UI.</summary>
    public event EventHandler<FeatureStoppedEventArgs>? FeatureStopped;

    public bool IsEnabled(Feature feature) => _running.ContainsKey(feature);

    public FeatureStatus GetStatus()
    {
        var proxy = _running.TryGetValue(Feature.SniProxy, out var running) ? running.Service as IRunningProxy : null;
        return new FeatureStatus(IsEnabled(Feature.DnsRouting), proxy is not null, IsEnabled(Feature.PubgMode), proxy?.Port, proxy?.ActiveConnections ?? 0);
    }

    public async Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnableCoreAsync(feature, allowProxyOverride).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Áp danh sách tên miền mới cho DNS/SNI đang chạy (spec KH8 3.1). Lỗi thì quay về danh sách cũ.</summary>
    public async Task<ReapplyResult> ReapplyAsync(IReadOnlyList<string> domains, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domains);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = RoutedFeatures.Where(_running.ContainsKey).ToList();
            if (domains.Count == 0 && active.Count > 0) return new ReapplyResult(false, true, FeatureError.NoDomains);
            var previous = _routed;
            _routed = domains;
            var failure = await RestartAsync(active).ConfigureAwait(false);
            if (failure is null) return new ReapplyResult(true, false);

            LogReapplyFailed(_logger, failure.Feature, failure.Error);
            _routed = previous;
            var restoreFailure = await RestartAsync(active).ConfigureAwait(false);
            if (restoreFailure is not null) LogReapplyRestoreFailed(_logger, restoreFailure.Feature, restoreFailure.Error);
            return new ReapplyResult(false, restoreFailure is null, failure.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Tắt một tính năng. Hoàn tác lỗi thì dịch vụ vẫn chạy (mạng không hỏng) và mục Journal được giữ (KH5 quyết định 6).</summary>
    public async Task<FeatureResult> DisableAsync(Feature feature)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await DisableCoreAsync(feature).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Nút "Tắt hết & khôi phục": tắt PUBG, Proxy, DNS, rồi hoàn tác mọi mục Journal còn lại.</summary>
    public async Task<DisableAllResult> DisableAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var results = new List<FeatureResult>();
            foreach (var feature in ShutdownOrder) results.Add(await DisableCoreAsync(feature).ConfigureAwait(false));
            if (_running.IsEmpty && _journal.Entries.Count > 0) _journal.RollbackAll();
            return new DisableAllResult(results, _journal.Entries.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Thoát app hoặc đăng xuất: hoàn tác toàn bộ Journal rồi dừng mọi dịch vụ, kể cả khi hoàn tác lỗi.
    /// Mục lỗi được giữ cho lần mở sau hoặc --cleanup (spec 4.5).
    /// </summary>
    public async Task<RollbackResult> ShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var result = _journal.RollbackAll();
            foreach (var feature in ShutdownOrder)
            {
                if (!_running.TryRemove(feature, out var running)) continue;
                running.Stopping = true;
                await running.Service.DisposeAsync().ConfigureAwait(false);
            }
            if (!result.Success) LogShutdownLeftovers(_logger, result.Failed.Count);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync() => await ShutdownAsync().ConfigureAwait(false);

    private async Task<FeatureResult> EnableCoreAsync(Feature feature, bool allowProxyOverride)
    {
        if (_running.ContainsKey(feature)) return new(feature, FeatureOutcome.Unchanged);
        if (feature != Feature.PubgMode && _routed.Count == 0) return new(feature, FeatureOutcome.Failed, FeatureError.NoDomains);

        IRunningService service;
        try
        {
            service = StartService(feature, port: null);
        }
        catch (Exception ex) when (ex is DnsStubException or SplitProxyException)
        {
            LogStartFailed(_logger, feature, ex);
            return new(feature, FeatureOutcome.Failed, StartError(feature, ex));
        }

        try
        {
            ApplySystem(feature, service, allowProxyOverride);
        }
        catch (Exception ex)
        {
            // Ranh giới của lệnh bật: hoàn tác phần vừa làm của chính tính năng này rồi báo lỗi (spec 6).
            var leftBehind = !_journal.Rollback(FeatureKeys.For(feature)).Success;
            await service.DisposeAsync().ConfigureAwait(false);
            if (ex is ConfiguratorException { Error: ConfiguratorError.ExistingProxyConfig } && !leftBehind)
                return new(feature, FeatureOutcome.NeedsProxyOverride);
            LogApplyFailed(_logger, feature, ex);
            return new(feature, FeatureOutcome.Failed, SystemError(ex), leftBehind);
        }

        var started = new Running(service, new RestartPolicy(_time));
        _running[feature] = started;
        Watch(feature, started);
        LogEnabled(_logger, feature);
        return new(feature, FeatureOutcome.Enabled);
    }

    /// <summary>Tắt rồi bật lại từng tính năng; trả kết quả lỗi đầu tiên, null nếu ổn. Người dùng đã đồng ý ghi đè proxy khi bật lần đầu.</summary>
    private async Task<FeatureResult?> RestartAsync(IReadOnlyList<Feature> features)
    {
        foreach (var feature in features)
        {
            if (_running.ContainsKey(feature))
            {
                var off = await DisableCoreAsync(feature).ConfigureAwait(false);
                if (off.Outcome != FeatureOutcome.Disabled) return off;
            }
            var on = await EnableCoreAsync(feature, allowProxyOverride: true).ConfigureAwait(false);
            if (on.Outcome != FeatureOutcome.Enabled) return on;
        }
        return null;
    }

    private async Task<FeatureResult> DisableCoreAsync(Feature feature)
    {
        var key = FeatureKeys.For(feature);
        if (!_running.TryGetValue(feature, out var running))
        {
            if (!_journal.Entries.Any(e => string.Equals(e.Feature, key, StringComparison.Ordinal)))
                return new(feature, FeatureOutcome.Unchanged);
            // Mục còn sót từ lần hoàn tác lỗi trước hoặc lần chạy trước: thử lại.
            return _journal.Rollback(key).Success
                ? new FeatureResult(feature, FeatureOutcome.Disabled)
                : new FeatureResult(feature, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true);
        }

        var result = _journal.Rollback(key);
        if (!result.Success)
        {
            LogRollbackFailed(_logger, feature, result.Failed.Count);
            return new(feature, FeatureOutcome.Failed, FeatureError.RollbackFailed, ChangesLeftBehind: true);
        }
        running.Stopping = true;
        _running.TryRemove(feature, out _);
        await running.Service.DisposeAsync().ConfigureAwait(false);
        LogDisabled(_logger, feature);
        return new(feature, FeatureOutcome.Disabled);
    }

    private IRunningService StartService(Feature feature, int? port)
    {
        var tuning = _tuning();
        return feature switch
        {
            Feature.DnsRouting => _services.StartDnsStub(_routed),
            Feature.SniProxy => _services.StartSniProxy(_routed, port, tuning),
            Feature.PubgMode => _services.StartPubgForwarder(FeatureDomains.Pubg, tuning),
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
        };
    }

    private void ApplySystem(Feature feature, IRunningService service, bool allowProxyOverride)
    {
        var key = FeatureKeys.For(feature);
        switch (feature)
        {
            case Feature.DnsRouting:
                _changes.AddDnsRoute(key, _routed, IPAddress.Loopback);
                break;
            case Feature.SniProxy:
                _changes.SetAutoProxy(key, ((IRunningProxy)service).PacUrl, allowProxyOverride);
                break;
            case Feature.PubgMode:
                _changes.AddHostsEntries(key, FeatureDomains.PubgHosts);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feature), feature, null);
        }
    }

    private void Watch(Feature feature, Running running) => _ = WatchAsync(feature, running);

    private async Task WatchAsync(Feature feature, Running running)
    {
        try
        {
            await running.Service.Completion.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Completion faulted = dịch vụ chết vì lỗi socket; xử lý như dừng bất ngờ ở dưới.
            LogServiceFaulted(_logger, feature, ex);
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (running.Stopping || !_running.TryGetValue(feature, out var current) || !ReferenceEquals(current, running)) return;

            // Giải phóng cổng và DohClient của dịch vụ đã chết trước khi mở lại trên cùng cổng.
            await running.Service.DisposeAsync().ConfigureAwait(false);
            var port = (running.Service as IRunningProxy)?.Port;
            while (running.Policy.TryRecordRestart())
            {
                try
                {
                    var restarted = new Running(StartService(feature, port), running.Policy);
                    _running[feature] = restarted;
                    Watch(feature, restarted);
                    LogRestarted(_logger, feature);
                    return;
                }
                catch (Exception ex) when (ex is DnsStubException or SplitProxyException)
                {
                    LogRestartFailed(_logger, feature, ex);
                }
            }

            _running.TryRemove(feature, out _);
            var result = _journal.Rollback(FeatureKeys.For(feature));
            LogGaveUp(_logger, feature);
            FeatureStopped?.Invoke(this, new FeatureStoppedEventArgs(feature, !result.Success));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static FeatureError StartError(Feature feature, Exception ex) => (feature, ex) switch
    {
        (Feature.DnsRouting, DnsStubException { Error: not DnsStubError.Other }) => FeatureError.DnsPortBusy,
        (Feature.SniProxy, SplitProxyException { Error: not ProxyStartError.Other }) => FeatureError.ProxyPortsBusy,
        (Feature.PubgMode, SplitProxyException { Error: not ProxyStartError.Other }) => FeatureError.PubgPortBusy,
        _ => FeatureError.ListenerFailed,
    };

    private static FeatureError SystemError(Exception ex) => ex is not ConfiguratorException configurator
        ? FeatureError.Other
        : configurator.Error switch
        {
            ConfiguratorError.AccessDenied => FeatureError.AccessDenied,
            ConfiguratorError.DnsRouteConflict => FeatureError.DnsRouteConflict,
            ConfiguratorError.ExistingProxyConfig => FeatureError.ExistingProxyConfig,
            ConfiguratorError.UserHiveNotLoaded => FeatureError.UserHiveNotLoaded,
            ConfiguratorError.UnexpectedRegistryValue => FeatureError.UnexpectedProxyValue,
            ConfiguratorError.HostsUnsupportedEncoding => FeatureError.HostsUnsupportedEncoding,
            ConfiguratorError.NrptFailed => FeatureError.NrptFailed,
            _ => FeatureError.Other,
        };

    private sealed class Running(IRunningService service, RestartPolicy policy)
    {
        public IRunningService Service { get; } = service;

        public RestartPolicy Policy { get; } = policy;

        /// <summary>Đặt khi tắt có chủ đích, để watchdog không mở lại.</summary>
        public bool Stopping { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã bật {Feature}")]
    private static partial void LogEnabled(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã tắt {Feature}")]
    private static partial void LogDisabled(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không mở được dịch vụ của {Feature}")]
    private static partial void LogStartFailed(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không áp dụng được {Feature}, đã hoàn tác phần vừa làm")]
    private static partial void LogApplyFailed(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hoàn tác {Feature} còn {Count} mục lỗi, giữ dịch vụ chạy")]
    private static partial void LogRollbackFailed(ILogger logger, Feature feature, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Thoát: còn {Count} mục Journal chưa hoàn tác, để lần mở sau hoặc --cleanup")]
    private static partial void LogShutdownLeftovers(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dịch vụ của {Feature} dừng vì lỗi")]
    private static partial void LogServiceFaulted(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Watchdog đã mở lại dịch vụ của {Feature}")]
    private static partial void LogRestarted(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Watchdog không mở lại được dịch vụ của {Feature}")]
    private static partial void LogRestartFailed(ILogger logger, Feature feature, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Dịch vụ của {Feature} chết quá 3 lần trong 1 phút, đã tắt tính năng")]
    private static partial void LogGaveUp(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Áp danh sách tên miền mới lỗi ở {Feature} ({Error}); quay về danh sách cũ.")]
    private static partial void LogReapplyFailed(ILogger logger, Feature feature, FeatureError error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không khôi phục được danh sách tên miền cũ cho {Feature} ({Error}); chạy 'lunadns cleanup' nếu mạng bất thường")]
    private static partial void LogReapplyRestoreFailed(ILogger logger, Feature feature, FeatureError error);
}
