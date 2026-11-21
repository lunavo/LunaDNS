using System.Net.Http.Headers;
using LunaDns.App.Features;
using LunaDns.Core.Domains;
using Microsoft.Extensions.Logging;

namespace LunaDns.App.DomainList;

internal enum DomainListCheckKind
{
    NetworkError,
    TooLarge,
    Offered,
    /// <summary>Công tắc tắt hoặc dịch vụ nền không dùng được: không tải.</summary>
    Skipped,
}

internal sealed record DomainListCheck(DateTimeOffset At, DomainListCheckKind Kind, DomainListOfferResult? Offer = null);

/// <summary>
/// Tải domains.json theo lịch của spec D4: 30 giây sau khi mở app, rồi 24 giờ/lần theo checkedAt của nơi giữ thật;
/// lỗi mạng thì 1 giờ. Không tự xác minh: byte đi nguyên vẹn tới IDomainListSink (Windows: app; macOS: daemon).
/// Lượt lỗi chỉ nằm trong bộ nhớ. Một lượt tải tại một thời điểm.
/// </summary>
// SemaphoreSlim không dùng AvailableWaitHandle thì không giữ handle nào; không dispose để lượt đang dở
// (Offer/Reapply không huỷ được) vẫn Release được khi app hay daemon đang tắt.
#pragma warning disable CA1001
internal sealed partial class DomainListUpdater(HttpClient http, Uri url, IDomainListSink sink, TimeProvider time, ILogger logger)
#pragma warning restore CA1001
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    public static readonly TimeSpan RetryDelay = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _single = new(1, 1);
    private int _reapplyFailures;
    private volatile DomainListCheck? _last;
    private volatile bool _checking;

    public static Uri DefaultUrl(string repository) => new($"https://github.com/{repository}/releases/download/domain-list/domains.json");

    public DomainListCheck? LastCheck => _last;

    public bool IsChecking => _checking;

    public int ReapplyFailures => _reapplyFailures;

    public event EventHandler? Changed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(StartupDelay, time, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            var wait = await TickAsync(cancellationToken).ConfigureAwait(false);
            await Task.Delay(wait, time, cancellationToken).ConfigureAwait(false);
        }
    }

    internal async Task<TimeSpan> TickAsync(CancellationToken cancellationToken)
    {
        var state = await sink.GetDomainListAsync().ConfigureAwait(false);
        if (state is null || !state.Enabled) return RetryDelay;
        if (state.CheckedAt is { } checkedAt)
        {
            // Đồng hồ từng chạy nhanh rồi được chỉnh lại thì checkedAt nằm ở tương lai: chặn trên bằng Interval,
            // nếu không Task.Delay quá ~49,7 ngày sẽ ném và vòng hẹn giờ chết hẳn.
            var due = checkedAt + Interval - time.GetUtcNow();
            if (due > Interval) return Interval;
            if (due > TimeSpan.Zero) return due;
        }
        var check = await CheckNowAsync(cancellationToken).ConfigureAwait(false);
        return NextDelay(check, _reapplyFailures);
    }

    public async Task<DomainListCheck> CheckNowAsync(CancellationToken cancellationToken = default)
    {
        await _single.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _checking = true;
            Changed?.Invoke(this, EventArgs.Empty);
            var check = await CheckCoreAsync(cancellationToken).ConfigureAwait(false);
            _reapplyFailures = check.Offer?.Outcome is DomainListOutcome.ReapplyFailed or DomainListOutcome.SaveFailed ? _reapplyFailures + 1 : 0;
            _last = check;
            return check;
        }
        finally
        {
            _checking = false;
            _single.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    internal static TimeSpan NextDelay(DomainListCheck check, int reapplyFailures) => check switch
    {
        { Kind: DomainListCheckKind.NetworkError or DomainListCheckKind.TooLarge or DomainListCheckKind.Skipped } => RetryDelay,
        { Offer.Outcome: DomainListOutcome.ReapplyFailed or DomainListOutcome.SaveFailed } => reapplyFailures >= 2 ? Interval : RetryDelay,
        { Offer.Outcome: DomainListOutcome.Unavailable or DomainListOutcome.Disabled } => RetryDelay,
        _ => Interval,
    };

    private async Task<DomainListCheck> CheckCoreAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        byte[]? body;
        try
        {
            body = await DownloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogDownloadFailed(logger, ex.GetType().Name);
            return new(now, DomainListCheckKind.NetworkError);
        }
        if (body is null) return new(now, DomainListCheckKind.TooLarge);
        var offer = await sink.OfferDomainListAsync(body).ConfigureAwait(false);
        return new(now, DomainListCheckKind.Offered, offer);
    }

    /// <summary>Đọc tối đa MaxBytes + 1 byte; vượt thì null. Không tin Content-Length.</summary>
    private async Task<byte[]?> DownloadAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LunaDNS", AppInfo.Version.ToString()));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[DomainListVerifier.MaxBytes + 1];
        var total = 0;
        int read;
        while (total < buffer.Length && (read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false)) > 0)
            total += read;
        return total > DomainListVerifier.MaxBytes ? null : buffer[..total];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Không tải được danh sách tên miền ({Reason}); thử lại sau.")]
    private static partial void LogDownloadFailed(ILogger logger, string reason);
}
