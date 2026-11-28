using System.Net.Sockets;
using LunaDns.App.Features;
using LunaDns.Core.Dns;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
using LunaDns.Core.Probing;
using LunaDns.Platform.MacOS.Daemon;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.MacOS;

/// <summary>
/// Bản macOS: giao diện chạy quyền user, không đổi hệ thống. Mọi bật/tắt/trạng thái đi qua socket tới daemon launchd (root).
/// Yêu cầu được xếp hàng (một kết nối mỗi lúc) để ảnh chụp trạng thái không bị phản hồi cũ ghi đè; lần poll của timer
/// gặp lúc đang bận thì bỏ qua. Daemon không trả lời thì mọi tính năng hiện là tắt và Availability cho biết vì sao.
/// </summary>
internal sealed partial class DaemonFeatureController : IFeatureController
{
    public const string PlistPath = "/Library/LaunchDaemons/" + DaemonProtocol.Label + ".plist";

    private static readonly FeatureStatus AllOff = new(false, false, false, null, 0);
    private static readonly RollbackResult NothingRolledBack = new(0, []);

    private readonly IDaemonTransport _transport;
    private readonly Func<bool> _isInstalled;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Version? _appVersion;
    private volatile Snapshot _snapshot;

    /// <summary>Chờ trước lần gửi lại duy nhất khi người dùng thoát mà dịch vụ nền không trả lời (B8). Test đặt 0.</summary>
    internal TimeSpan QuitRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Phiên bản daemon lúc nó trả "Lệnh lạ:"; khác bản hiện tại (đã cài lại) thì cờ tự hết.</summary>
    private volatile string? _unknownCommandVersion;

    public DaemonFeatureController(IDaemonTransport transport, Func<bool> isInstalled, ILoggerFactory loggerFactory, Version? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(isInstalled);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _transport = transport;
        _isInstalled = isInstalled;
        _logger = loggerFactory.CreateLogger<DaemonFeatureController>();
        _appVersion = appVersion ?? typeof(DaemonFeatureController).Assembly.GetName().Version;
        _snapshot = new Snapshot(ControllerAvailability.DaemonConnecting, AllOff, null, null, null);
    }

    public static DaemonFeatureController CreateDefault(ILoggerFactory loggerFactory) =>
        new(new SocketDaemonTransport(), () => File.Exists(PlistPath), loggerFactory);

    /// <summary>Watchdog của dịch vụ nền đã dừng một tính năng (F5): phát khi Stopped.Seq vượt mốc, ngoài khoá _gate.</summary>
    public event EventHandler<FeatureStoppedEventArgs>? FeatureStopped;

    // Mốc Stopped.Seq (F5): null = chưa thấy status nào. Không đặt lại khi mất kết nối, để nối lại không báo trùng.
    private long? _stoppedSeq;
    // Lần dừng mới thấy trong lúc giữ _gate; phát sau khi nhả khoá.
    private FeatureStoppedEventArgs? _pendingStop;

    public ControllerAvailability Availability => _snapshot.Availability;

    public FeatureStatus Status => _snapshot.Status;

    public RollbackResult StartupRollback => NothingRolledBack;

    public int? JournalEntries => _snapshot.JournalEntries;

    /// <summary>Từ trường resume của câu trả lời gần nhất; null với daemon cũ hoặc khi daemon không trả lời.</summary>
    public ResumeState? Resume => _snapshot.Resume;

    /// <summary>Phiên bản daemon đang chạy (từ ping/status), để ghi vào báo cáo.</summary>
    public string? DaemonVersion => _snapshot.Version;

    /// <summary>
    /// Daemon đang chạy nhưng cũ hơn giao diện (so x.y.z, bỏ phần "+commit"), hoặc đã trả "Lệnh lạ:" cho một lệnh mới:
    /// giao diện hiện nút cài lại. Không đọc được phiên bản thì chỉ dựa vào "Lệnh lạ:".
    /// </summary>
    public bool ServiceOutdated
    {
        get
        {
            var snapshot = _snapshot;
            if (snapshot.Availability != ControllerAvailability.DaemonRunning) return false;
            if (_unknownCommandVersion is { } seen && seen == (snapshot.Version ?? string.Empty)) return true;
            return IsOlderThanApp(snapshot.Version);
        }
    }

    private bool IsOlderThanApp(string? daemonVersion)
    {
        if (_appVersion is null || daemonVersion is null) return false;
        var plus = daemonVersion.IndexOf('+', StringComparison.Ordinal);
        if (!Version.TryParse(plus < 0 ? daemonVersion : daemonVersion[..plus], out var daemon)) return false;
        static Version Core(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
        return Core(daemon) < Core(_appVersion);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            await SendCoreAsync(new DaemonRequest(DaemonProtocol.Status), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaisePendingStop();
    }

    public async Task<FeatureResult> EnableAsync(Feature feature, bool allowProxyOverride = false)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.Enable, FeatureKeys.For(feature), allowProxyOverride)).ConfigureAwait(false);
        return ToResult(feature, response);
    }

    public async Task<FeatureResult> DisableAsync(Feature feature)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.Disable, FeatureKeys.For(feature))).ConfigureAwait(false);
        return ToResult(feature, response);
    }

    public async Task<DisableAllResult> DisableAllAsync()
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.DisableAll)).ConfigureAwait(false);
        return new DisableAllResult([], Remaining(response));
    }

    public async Task<DomainSelection?> GetDomainsAsync() =>
        (await SendAsync(new DaemonRequest(DaemonProtocol.GetDomains)).ConfigureAwait(false))?.Domains;

    /// <summary>Dịch vụ nền tự kiểm, áp và lưu. Bản cũ chưa biết set-domains trả "Lệnh lạ:" nên đề nghị cài lại.</summary>
    public async Task<DomainApplyResult> ApplyDomainsAsync(DomainSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetDomains, Domains: selection)).ConfigureAwait(false);
        if (response is null) return new(false, ErrorMessages.For(FeatureError.ServiceUnavailable));
        if (response.Ok) return new(true, null);
        if (IsUnknownCommand(response)) return new(false, Strings.DaemonNeedsUpdate);
        var error = Enum.TryParse<FeatureError>(response.Error, out var parsed) && Enum.IsDefined(parsed) ? parsed : (FeatureError?)null;
        return new(false, response.Message, error);
    }

    public async Task<TuningState?> GetTuningAsync() =>
        await SendAsync(new DaemonRequest(DaemonProtocol.GetTuning)).ConfigureAwait(false) is { Ok: true, Tuning: { } tuning }
            ? new TuningState(tuning.Doh, tuning.ToCandidate())
            : null;

    /// <summary>
    /// Dịch vụ nền tự kiểm, áp, lưu và tự quay lại khi lỗi, nên bỏ qua restore. Bản cũ chưa biết set-tuning trả "Lệnh lạ:"
    /// nên đề nghị cài lại.
    /// </summary>
    public async Task<TuningApplyResult> ApplyTuningAsync(DohProvider doh, TuneCandidate candidate, Action? restore = null)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetTuning, Tuning: DaemonTuning.From(doh, candidate))).ConfigureAwait(false);
        if (response is null) return new(false, ErrorMessages.For(FeatureError.ServiceUnavailable));
        if (response.Ok) return new(true, null);
        if (IsUnknownCommand(response)) return new(false, Strings.DaemonNeedsUpdateTuning);
        var error = Enum.TryParse<FeatureError>(response.Error, out var parsed) && Enum.IsDefined(parsed) ? parsed : (FeatureError?)null;
        return new(false, response.Message, error);
    }

    /// <summary>null khi daemon không trả lời hoặc là bản cũ chưa biết get-domain-list ("Lệnh lạ:"): giao diện ẩn dòng mới.</summary>
    public async Task<DomainListState?> GetDomainListAsync() =>
        (await SendAsync(new DaemonRequest(DaemonProtocol.GetDomainList)).ConfigureAwait(false)) is { Ok: true, DomainList: { } list }
            ? list.ToState()
            : null;

    /// <summary>Gửi nguyên byte file đã tải; daemon xác minh lại từ đầu. Không trả lời hoặc daemon cũ thì Unavailable.</summary>
    public async Task<DomainListOfferResult> OfferDomainListAsync(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetDomainList, Envelope: Convert.ToBase64String(file))).ConfigureAwait(false);
        return response?.DomainListResult?.ToResult() ?? new DomainListOfferResult(DomainListOutcome.Unavailable);
    }

    public async Task<DomainListOfferResult> SetDomainListEnabledAsync(bool enabled)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetDomainListEnabled, Enabled: enabled)).ConfigureAwait(false);
        return response?.DomainListResult?.ToResult() ?? new DomainListOfferResult(DomainListOutcome.Unavailable);
    }

    /// <summary>
    /// Đuôi log của dịch vụ nền cho báo cáo Chẩn đoán (F3); daemon đã che tên. Không lấy được thì một dòng giải thích:
    /// daemon cũ chưa biết get-log ("Lệnh lạ:", cũng làm ServiceOutdated bật), không trả lời, hoặc từ chối.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetServiceLogAsync()
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.GetLog)).ConfigureAwait(false);
        if (response is null) return [Strings.ServiceLogUnavailable];
        if (response.Ok) return response.Log ?? [];
        if (IsUnknownCommand(response)) return [Strings.ServiceLogOutdated];
        return [Strings.Format(nameof(Strings.ServiceLogRefusedFormat), response.Message ?? string.Empty)];
    }

    /// <summary>Daemon tự bật lại lúc khởi động; giao diện không làm gì.</summary>
    public Task ResumeAsync() => Task.CompletedTask;

    /// <summary>Daemon cũ chưa biết set-auto-resume trả "Lệnh lạ:" nên đề nghị cài lại.</summary>
    public async Task<string?> SetAutoResumeAsync(bool enabled)
    {
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.SetAutoResume, Enabled: enabled)).ConfigureAwait(false);
        if (response is null) return ErrorMessages.For(FeatureError.ServiceUnavailable);
        if (response.Ok) return null;
        return IsUnknownCommand(response) ? Strings.DaemonNeedsUpdate : response.Message ?? Strings.AutoResumeSaveFailed;
    }

    /// <summary>
    /// Chưa cài, hoặc tài khoản khác đang ngồi trước máy (B4): không gửi. Daemon đang chạy mà không có gì bật (và, khi người
    /// dùng thoát, không còn wanted để xoá): không làm phiền. Còn lại (kể cả lần poll gần nhất lỗi, B8) thì gửi disable-all;
    /// người dùng thoát mà không có trả lời thì gửi lại một lần. Không có trả lời → <see cref="ShutdownOutcome.Unknown"/>.
    /// Hết phiên thì gửi kèm SessionEnd để daemon giữ wanted (F1), một lần, không thử lại (giới hạn 4 giây của App).
    /// </summary>
    public async Task<int> ShutdownAsync(ShutdownReason reason)
    {
        var snapshot = _snapshot;
        if (snapshot.Availability is ControllerAvailability.DaemonNotInstalled or ControllerAvailability.DaemonOtherUser) return 0;
        if (snapshot.Availability == ControllerAvailability.DaemonRunning)
        {
            var wantedLeft = reason == ShutdownReason.UserQuit && snapshot.Resume is { } resume && !resume.Wanted.IsEmpty();
            if (!snapshot.Status.DnsRouting && !snapshot.Status.SniProxy && snapshot.JournalEntries is 0 or null && !wantedLeft) return 0;
        }
        else if (reason == ShutdownReason.SessionEnd)
        {
            return 0;
        }

        var request = new DaemonRequest(DaemonProtocol.DisableAll, SessionEnd: reason == ShutdownReason.SessionEnd ? true : null);
        var response = await SendAsync(request).ConfigureAwait(false);
        if (response is null && reason == ShutdownReason.UserQuit)
        {
            await Task.Delay(QuitRetryDelay).ConfigureAwait(false);
            response = await SendAsync(request).ConfigureAwait(false);
        }
        return response is null ? ShutdownOutcome.Unknown : Remaining(response);
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<DaemonResponse?> SendAsync(DaemonRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        DaemonResponse? response;
        try
        {
            response = await SendCoreAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
        RaisePendingStop();
        return response;
    }

    private void NoteStopped(DaemonStopped? stopped)
    {
        var seq = stopped?.Seq ?? 0;
        var baseline = _stoppedSeq;
        _stoppedSeq = seq;
        // Lần đầu thấy, hoặc daemon khởi động lại (Seq nhỏ hơn mốc): chỉ lấy mốc. Banner U1 lo lần dừng trước khi app mở.
        if (baseline is not { } previous || seq <= previous) return;
        if (!FeatureKeys.TryParse(stopped!.Feature, out var feature)) return;
        _pendingStop = new FeatureStoppedEventArgs(feature, stopped.ChangesLeftBehind);
    }

    private void RaisePendingStop()
    {
        var stop = Interlocked.Exchange(ref _pendingStop, null);
        if (stop is not null) FeatureStopped?.Invoke(this, stop);
    }

    /// <summary>null khi không nói chuyện được với daemon (Availability đã cập nhật).</summary>
    private async Task<DaemonResponse?> SendCoreAsync(DaemonRequest request, CancellationToken cancellationToken)
    {
        DaemonResponse response;
        try
        {
            response = await _transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is DaemonUnavailableException or SocketException or IOException or OperationCanceledException
            or System.Text.Json.JsonException)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            var availability = _isInstalled() ? ControllerAvailability.DaemonNotResponding : ControllerAvailability.DaemonNotInstalled;
            if (_snapshot.Availability != availability) LogUnavailable(_logger, availability, ex);
            _snapshot = new Snapshot(availability, AllOff, null, null, null);
            return null;
        }

        if (response.Status is { } s)
        {
            _snapshot = new Snapshot(ControllerAvailability.DaemonRunning,
                new FeatureStatus(s.DnsRouting, s.SniProxy, PubgMode: false, s.ProxyPort, s.ProxyConnections,
                    s.DohServer is { } doh ? new DohActivity(doh, s.DohFallback == true) : null),
                s.JournalEntries, response.Version ?? _snapshot.Version, response.Resume?.ToState());
            NoteStopped(s.Stopped);
        }
        else if (IsUnknownCommand(response))
        {
            // Daemon cũ chưa biết lệnh mới (vd. get-domains): nó vẫn chạy, giữ nguyên ảnh chụp trạng thái, ghi nhớ để đề nghị cài lại.
            _unknownCommandVersion = _snapshot.Version ?? string.Empty;
            LogDaemonFailed(_logger, request.Command, response.Message);
        }
        else if (!response.Ok)
        {
            // Không kèm trạng thái: daemon từ chối (tài khoản khác đang ngồi trước máy, B4) hoặc yêu cầu lỗi.
            var availability = DaemonProtocol.IsNotConsoleUser(response)
                ? ControllerAvailability.DaemonOtherUser
                : ControllerAvailability.DaemonNotResponding;
            if (_snapshot.Availability != availability) LogRejected(_logger, request.Command, response.Message);
            _snapshot = new Snapshot(availability, AllOff, null, null, null);
        }
        if (!response.Ok && response.Status is not null) LogDaemonFailed(_logger, request.Command, response.Message);
        return response;
    }

    private static bool IsUnknownCommand(DaemonResponse response) =>
        !response.Ok && response.Message?.StartsWith(DaemonProtocol.UnknownCommandPrefix, StringComparison.Ordinal) == true;

    private static int Remaining(DaemonResponse? response) => response switch
    {
        null => 1,
        { Ok: true } => 0,
        { Status: { } s } => Math.Max(s.JournalEntries, 1),
        _ => 1,
    };

    internal static FeatureResult ToResult(Feature feature, DaemonResponse? response)
    {
        if (response is null) return new FeatureResult(feature, FeatureOutcome.Failed, FeatureError.ServiceUnavailable);
        // Message của daemon đi theo dạng Detail; ErrorMessages.Reason chỉ hiện nó cho mã Other (B2).
        if (response.Outcome is null)
            return new FeatureResult(feature, FeatureOutcome.Failed, response.Status is null ? FeatureError.ServiceUnavailable : FeatureError.Other,
                Detail: response.Message);
        var outcome = Enum.TryParse<FeatureOutcome>(response.Outcome, out var o) ? o : FeatureOutcome.Failed;
        var error = response.Error is null ? FeatureError.None
            : Enum.TryParse<FeatureError>(response.Error, out var e) ? e : FeatureError.Other;
        if (outcome == FeatureOutcome.Failed && error == FeatureError.None) error = FeatureError.Other;
        return new FeatureResult(feature, outcome, error, ChangesLeftBehind: error == FeatureError.RollbackFailed, Detail: response.Message);
    }

    private sealed record Snapshot(ControllerAvailability Availability, FeatureStatus Status, int? JournalEntries, string? Version, ResumeState? Resume);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không nói chuyện được với dịch vụ nền ({Availability}).")]
    private static partial void LogUnavailable(ILogger logger, ControllerAvailability availability, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dịch vụ nền từ chối lệnh {Command}: {Message}")]
    private static partial void LogRejected(ILogger logger, string command, string? message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dịch vụ nền báo lỗi cho lệnh {Command}: {Message}")]
    private static partial void LogDaemonFailed(ILogger logger, string command, string? message);
}
