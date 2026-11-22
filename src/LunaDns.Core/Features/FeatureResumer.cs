using LunaDns.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Features;

/// <summary>
/// F1 (spec 2026-11-22-lunadns-auto-resume-design): nhớ tính năng người dùng để bật và bật lại lúc khởi động
/// (daemon macOS; lúc mở app trên Windows). Wanted chỉ đổi qua <see cref="Record"/> / <see cref="Forget"/>, do bên gọi gọi
/// sau lệnh tường minh; SIGTERM, đăng xuất, watchdog không đụng tới nó. Tự bật không bao giờ ghi đè proxy/PAC của máy.
/// </summary>
public sealed partial class FeatureResumer
{
    /// <summary>Lúc khởi động máy mạng và configd có thể chưa sẵn sàng: thử lại sau 5 rồi 15 giây.</summary>
    public static IReadOnlyList<TimeSpan> DefaultRetryDelays { get; } = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

    private readonly FeatureSet _features;
    private readonly Func<AppSettings> _settings;
    private readonly Action<AppSettings> _save;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private CancellationTokenSource? _run;
    private volatile bool _restoring;
    private volatile FeatureResult? _failure;

    /// <param name="save">Ghi settings.json (có thể ném IOException/UnauthorizedAccessException; lỗi chỉ được log).</param>
    public FeatureResumer(FeatureSet features, Func<AppSettings> settings, Action<AppSettings> save,
        IReadOnlyList<TimeSpan>? retryDelays = null, TimeProvider? time = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(save);
        _features = features;
        _settings = settings;
        _save = save;
        _retryDelays = retryDelays ?? DefaultRetryDelays;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public ResumeState State
    {
        get
        {
            var settings = _settings();
            return new ResumeState(settings.AutoResume, settings.Wanted ?? WantedFeatures.None, _restoring, _failure);
        }
    }

    /// <summary>
    /// Bật lại wanted nếu công tắc bật. <paramref name="rollbackClean"/> = false (journal còn mục cũ) thì không bật gì và ghi
    /// lỗi RollbackFailed. Bật lỗi thì thử lại theo retryDelays; NeedsProxyOverride, NoDomains không thử lại.
    /// <see cref="Record"/>, <see cref="Forget"/> hoặc huỷ token thì dừng.
    /// </summary>
    public async Task RunAsync(bool rollbackClean, CancellationToken cancellationToken = default)
    {
        var settings = _settings();
        var wanted = settings.Wanted ?? WantedFeatures.None;
        if (wanted.IsEmpty() || !settings.AutoResume) return;
        if (!rollbackClean)
        {
            _failure = new FeatureResult(wanted.List()[0], FeatureOutcome.Failed, FeatureError.RollbackFailed);
            LogBlocked(_logger);
            return;
        }

        CancellationTokenSource run;
        lock (_lock)
        {
            _run?.Cancel();
            _run = run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _restoring = true;
        }
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var failure = await EnableMissingAsync(run.Token).ConfigureAwait(false);
                _failure = failure;
                if (failure is null || !Retryable(failure) || attempt >= _retryDelays.Count) break;
                await Task.Delay(_retryDelays[attempt], _time, run.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // Lệnh tường minh hoặc dịch vụ đang tắt: bỏ dở.
        }
        finally
        {
            lock (_lock)
            {
                if (ReferenceEquals(_run, run))
                {
                    _run = null;
                    _restoring = false;
                }
            }
            run.Dispose();
        }
    }

    /// <summary>Gọi sau lệnh bật (thành công) hoặc tắt (thành công) của người dùng. PUBG bị bỏ qua.</summary>
    public void Record(Feature feature, bool on)
    {
        if (feature is not (Feature.DnsRouting or Feature.SniProxy)) return;
        StopRun();
        if (_failure?.Feature == feature) _failure = null;
        Save(s => s with { Wanted = (s.Wanted ?? WantedFeatures.None).With(feature, on) });
    }

    /// <summary>Tắt hết &amp; khôi phục, Thoát &amp; khôi phục, Cmd+Q: lần khởi động sau không tự bật gì.</summary>
    public void Forget()
    {
        StopRun();
        _failure = null;
        Save(s => s with { Wanted = WantedFeatures.None });
    }

    /// <summary>Công tắc Tự bật lại. false nếu không ghi được settings.json.</summary>
    public bool SetEnabled(bool enabled) => Save(s => s with { AutoResume = enabled });

    private async Task<FeatureResult?> EnableMissingAsync(CancellationToken cancellationToken)
    {
        FeatureResult? failure = null;
        foreach (var feature in (_settings().Wanted ?? WantedFeatures.None).List())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_features.IsEnabled(feature)) continue;
            var result = await _features.EnableAsync(feature, allowProxyOverride: false, cancellationToken).ConfigureAwait(false);
            if (result.Outcome is FeatureOutcome.Enabled or FeatureOutcome.Unchanged)
            {
                LogResumed(_logger, feature);
                continue;
            }
            LogResumeFailed(_logger, feature, result.Outcome, result.Error);
            failure ??= result;
        }
        return failure;
    }

    private static bool Retryable(FeatureResult failure) =>
        failure.Outcome == FeatureOutcome.Failed && failure.Error is not (FeatureError.NoDomains or FeatureError.RollbackFailed);

    private void StopRun()
    {
        lock (_lock)
        {
            if (_run is null) return;
            _run.Cancel();
            _run = null;
            _restoring = false;
        }
    }

    private bool Save(Func<AppSettings, AppSettings> change)
    {
        var current = _settings();
        var next = change(current);
        if (next == current) return true;
        try
        {
            _save(next);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogSaveFailed(_logger, ex);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tự bật lại {Feature}.")]
    private static partial void LogResumed(ILogger logger, Feature feature);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tự bật lại {Feature} không được: {Outcome} {Error}.")]
    private static partial void LogResumeFailed(ILogger logger, Feature feature, FeatureOutcome outcome, FeatureError error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Còn thay đổi cũ chưa hoàn tác; không tự bật lại.")]
    private static partial void LogBlocked(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Không lưu được trạng thái Tự bật lại vào settings.json.")]
    private static partial void LogSaveFailed(ILogger logger, Exception error);
}
