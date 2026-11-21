using System.Net.Sockets;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using LunaDns.Core.Features;
using LunaDns.Core.Journal;
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
    private volatile Snapshot _snapshot;

    public DaemonFeatureController(IDaemonTransport transport, Func<bool> isInstalled, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(isInstalled);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _transport = transport;
        _isInstalled = isInstalled;
        _logger = loggerFactory.CreateLogger<DaemonFeatureController>();
        _snapshot = new Snapshot(ControllerAvailability.DaemonConnecting, AllOff, null, null);
    }

    public static DaemonFeatureController CreateDefault(ILoggerFactory loggerFactory) =>
        new(new SocketDaemonTransport(), () => File.Exists(PlistPath), loggerFactory);

    // Daemon không báo watchdog dừng tính năng; lần poll sau chỉ thấy tính năng đã tắt.
    public event EventHandler<FeatureStoppedEventArgs>? FeatureStopped
    {
        add { }
        remove { }
    }

    public ControllerAvailability Availability => _snapshot.Availability;

    public FeatureStatus Status => _snapshot.Status;

    public RollbackResult StartupRollback => NothingRolledBack;

    public int? JournalEntries => _snapshot.JournalEntries;

    /// <summary>Phiên bản daemon đang chạy (từ ping/status), để ghi vào báo cáo.</summary>
    public string? DaemonVersion => _snapshot.Version;

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

    /// <summary>Không có gì bật thì không làm phiền daemon; daemon không chạy thì không còn gì để giao diện hoàn tác.</summary>
    public async Task<int> ShutdownAsync()
    {
        var snapshot = _snapshot;
        if (snapshot.Availability != ControllerAvailability.DaemonRunning) return 0;
        if (!snapshot.Status.DnsRouting && !snapshot.Status.SniProxy && snapshot.JournalEntries is 0 or null) return 0;
        var response = await SendAsync(new DaemonRequest(DaemonProtocol.DisableAll)).ConfigureAwait(false);
        return response is null ? 0 : Remaining(response);
    }

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<DaemonResponse?> SendAsync(DaemonRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SendCoreAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
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
            _snapshot = new Snapshot(availability, AllOff, null, null);
            return null;
        }

        if (response.Status is { } s)
        {
            _snapshot = new Snapshot(ControllerAvailability.DaemonRunning,
                new FeatureStatus(s.DnsRouting, s.SniProxy, PubgMode: false, s.ProxyPort, s.ProxyConnections),
                s.JournalEntries, response.Version ?? _snapshot.Version);
        }
        else if (IsUnknownCommand(response))
        {
            // Daemon cũ chưa biết lệnh mới (vd. get-domains): nó vẫn chạy, giữ nguyên ảnh chụp trạng thái.
            LogDaemonFailed(_logger, request.Command, response.Message);
        }
        else if (!response.Ok)
        {
            // Không kèm trạng thái: daemon từ chối (UID khác chủ /dev/console) hoặc yêu cầu lỗi.
            LogRejected(_logger, request.Command, response.Message);
            _snapshot = new Snapshot(ControllerAvailability.DaemonNotResponding, AllOff, null, null);
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
        if (response.Outcome is null)
            return new FeatureResult(feature, FeatureOutcome.Failed, response.Status is null ? FeatureError.ServiceUnavailable : FeatureError.Other);
        var outcome = Enum.TryParse<FeatureOutcome>(response.Outcome, out var o) ? o : FeatureOutcome.Failed;
        var error = response.Error is null ? FeatureError.None
            : Enum.TryParse<FeatureError>(response.Error, out var e) ? e : FeatureError.Other;
        if (outcome == FeatureOutcome.Failed && error == FeatureError.None) error = FeatureError.Other;
        return new FeatureResult(feature, outcome, error, ChangesLeftBehind: error == FeatureError.RollbackFailed);
    }

    private sealed record Snapshot(ControllerAvailability Availability, FeatureStatus Status, int? JournalEntries, string? Version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không nói chuyện được với dịch vụ nền ({Availability}).")]
    private static partial void LogUnavailable(ILogger logger, ControllerAvailability availability, Exception error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dịch vụ nền từ chối lệnh {Command}: {Message}")]
    private static partial void LogRejected(ILogger logger, string command, string? message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dịch vụ nền báo lỗi cho lệnh {Command}: {Message}")]
    private static partial void LogDaemonFailed(ILogger logger, string command, string? message);
}
