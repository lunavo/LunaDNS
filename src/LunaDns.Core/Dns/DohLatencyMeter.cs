using Microsoft.Extensions.Logging;

namespace LunaDns.Core.Dns;

/// <summary>Kết quả đo một máy DoH: <see cref="Median"/> có khi đo được, <see cref="Error"/> có khi lỗi.</summary>
public sealed record DohLatency(DohProvider Provider, TimeSpan? Median, string? Error);

/// <summary>
/// Nút "Đo" (D2): mỗi máy một upstream riêng (không rơi sang dự phòng, kết nối mới), 1 truy vấn khởi động
/// (gồm TCP + TLS) rồi <see cref="MeasuredQueries"/> truy vấn A; điểm là trung vị các truy vấn sau, vì lúc dùng thật
/// kết nối được giữ 5 phút.
/// </summary>
public sealed class DohLatencyMeter(Func<DohServer, IDnsUpstream> upstreamFor, TimeProvider? time = null)
{
    public const string ProbeHost = "store.steampowered.com";
    public const int MeasuredQueries = 3;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Đo thật: <see cref="DohClient"/> chỉ chứa một máy, timeout mặc định của nó.</summary>
    public static DohLatencyMeter Create(ILoggerFactory? loggers = null) =>
        new(server => DohClient.Create([server], loggers?.CreateLogger<DohClient>()));

    public Task<IReadOnlyList<DohLatency>> MeasureAsync(IReadOnlyList<DohChoice> choices, CancellationToken cancellationToken) =>
        MeasureAsync(choices, Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>
    /// Hết <paramref name="timeout"/> tổng thì máy chưa xong ra "lỗi", máy đã đo xong vẫn giữ kết quả.
    /// Chỉ ném <see cref="OperationCanceledException"/> khi người gọi huỷ thật (ví dụ đóng app).
    /// </summary>
    public async Task<IReadOnlyList<DohLatency>> MeasureAsync(IReadOnlyList<DohChoice> choices, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choices);
        using var deadline = new CancellationTokenSource(timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var results = await Task.WhenAll(choices.Select(c => MeasureOneAsync(c, linked.Token, cancellationToken))).ConfigureAwait(false);
        // OrderBy ổn định: máy bằng điểm giữ thứ tự danh mục.
        return [.. results.OrderBy(r => r.Median is null).ThenBy(r => r.Median ?? TimeSpan.Zero)];
    }

    private async Task<DohLatency> MeasureOneAsync(DohChoice choice, CancellationToken token, CancellationToken callerToken)
    {
        var upstream = upstreamFor(choice.Server);
        try
        {
            await QueryAsync(upstream, token).ConfigureAwait(false);
            var samples = new TimeSpan[MeasuredQueries];
            for (var i = 0; i < samples.Length; i++)
            {
                var start = _time.GetTimestamp();
                await QueryAsync(upstream, token).ConfigureAwait(false);
                samples[i] = _time.GetElapsedTime(start);
            }
            Array.Sort(samples);
            return new DohLatency(choice.Provider, samples[MeasuredQueries / 2], null);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested && token.IsCancellationRequested)
        {
            return new DohLatency(choice.Provider, null, "Hết thời gian đo.");
        }
        catch (Exception ex) when (ex is DohException or HttpRequestException or IOException
                                   || (ex is OperationCanceledException && !callerToken.IsCancellationRequested))
        {
            return new DohLatency(choice.Provider, null, ex.Message);
        }
        finally
        {
            (upstream as IDisposable)?.Dispose();
        }
    }

    private static async Task QueryAsync(IDnsUpstream upstream, CancellationToken cancellationToken)
    {
        var response = await upstream.QueryAsync(DnsWire.BuildQuery(ProbeHost, DnsWire.TypeA), cancellationToken).ConfigureAwait(false);
        var rcode = DnsWire.ReadRcode(response);
        if (rcode != DnsRcode.NoError) throw new DohException($"Máy chủ trả mã lỗi {rcode}.");
        if (DnsWire.ReadAddresses(response).Count == 0) throw new DohException("Máy chủ không trả bản ghi A.");
    }
}
