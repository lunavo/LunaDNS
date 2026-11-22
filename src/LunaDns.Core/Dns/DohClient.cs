using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using LunaDns.Core.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>
/// DoH theo RFC 8484 (POST, application/dns-message). Các máy chạy đua so le (<see cref="DefaultHedgeDelay"/>), mỗi máy
/// có timeout riêng. Máy lỗi, hoặc chậm hơn máy mở sau nó, nghỉ <see cref="DefaultCooldown"/>: trong lúc đó máy khoẻ được
/// hỏi trước (B3/F4, spec doh-failover).
/// </summary>
public sealed partial class DohClient : IDnsUpstream, IDisposable
{
    public const string MediaType = "application/dns-message";
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultHedgeDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromMinutes(5);
    private const int MaxResponseBytes = ushort.MaxValue;

    private readonly HttpMessageInvoker _http;
    private readonly IReadOnlyList<DohServer> _servers;
    private readonly TimeSpan _timeout;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _hedgeDelay;
    private readonly TimeSpan _cooldown;
    private readonly object _health = new();
    private readonly DateTimeOffset[] _downUntil;
    private int _lastAnswered = -1;

    public DohClient(HttpMessageHandler handler, IReadOnlyList<DohServer> servers, TimeSpan? timeout = null, ILogger<DohClient>? logger = null,
        TimeProvider? time = null, TimeSpan? hedgeDelay = null, TimeSpan? cooldown = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(servers);
        if (servers.Count == 0) throw new ArgumentException("Cần ít nhất một máy chủ DoH.", nameof(servers));
        _http = new HttpMessageInvoker(handler, disposeHandler: true);
        _servers = servers;
        _timeout = timeout ?? DefaultTimeout;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _time = time ?? TimeProvider.System;
        _hedgeDelay = hedgeDelay ?? DefaultHedgeDelay;
        _cooldown = cooldown ?? DefaultCooldown;
        _downUntil = new DateTimeOffset[servers.Count];
    }

    /// <summary>Máy trả lời gần nhất; null khi chưa có câu trả lời nào.</summary>
    public DohActivity? Activity
    {
        get
        {
            lock (_health) return _lastAnswered < 0 ? null : new DohActivity(_servers[_lastAnswered].Name, _lastAnswered != 0);
        }
    }

    /// <summary>Client thật: không qua proxy hệ thống, nối thẳng tới IP bootstrap của từng máy chủ.</summary>
    public static DohClient Create(IReadOnlyList<DohServer> servers, ILogger<DohClient>? logger = null)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = DefaultTimeout,
            ConnectCallback = (context, ct) => ConnectToBootstrapAsync(servers, context, ct),
        };
        return new DohClient(handler, servers, logger: logger);
    }

    public async Task<byte[]> QueryAsync(ReadOnlyMemory<byte> query, CancellationToken cancellationToken)
    {
        if (!DnsWire.TryParseQuery(query.Span, out var parsed))
            throw new ArgumentException("Truy vấn DNS sai định dạng.", nameof(query));
        var body = query.ToArray();
        body[0] = 0; // RFC 8484 §4.1: ID = 0 để cache HTTP dùng chung được
        body[1] = 0;

        var order = Order();
        var result = await StaggeredRace.RunAsync(order.Length, _hedgeDelay, _time,
            (slot, ct) => AttemptAsync(_servers[order[slot]], body, parsed.Question, ct),
            ex => ex is HttpRequestException or DohException or IOException
                  || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested),
            cancellationToken,
            onFailure: (slot, ex) => MarkDown(order[slot], ex)).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new DohException("Mọi máy chủ DoH đều lỗi.", new AggregateException(result.Failures));
        MarkAnswered(order, result.Winner);
        return result.Value!;
    }

    public void Dispose() => _http.Dispose();

    private async Task<byte[]> AttemptAsync(DohServer server, byte[] body, DnsQuestion question, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(_timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var response = await SendAsync(server, body, linked.Token).ConfigureAwait(false);
        if (!DnsWire.IsResponseTo(response, question))
            throw new DohException($"{server.Name} trả response không khớp truy vấn.");
        return response;
    }

    /// <summary>Máy khoẻ trước, máy đang nghỉ sau; trong mỗi nhóm giữ thứ tự cấu hình (OrderBy ổn định).</summary>
    private int[] Order()
    {
        var now = _time.GetUtcNow();
        lock (_health)
            return Enumerable.Range(0, _servers.Count).OrderBy(i => _downUntil[i] > now ? 1 : 0).ToArray();
    }

    private void MarkDown(int index, Exception exception)
    {
        var until = _time.GetUtcNow() + _cooldown;
        lock (_health) _downUntil[index] = until;
        var name = _servers[index].Name;
        LogServerFailed(_logger, name, exception);
    }

    /// <summary>Máy thắng hết nghỉ. Máy mở trước nó mà chưa xong là chậm hơn cả máy mở sau: cho nghỉ như máy lỗi.</summary>
    private void MarkAnswered(int[] order, int winnerSlot)
    {
        var winner = order[winnerSlot];
        var until = _time.GetUtcNow() + _cooldown;
        bool changed;
        lock (_health)
        {
            for (var slot = 0; slot < winnerSlot; slot++) _downUntil[order[slot]] = until;
            _downUntil[winner] = default;
            changed = _lastAnswered != winner;
            _lastAnswered = winner;
        }
        if (!changed) return;
        var name = _servers[winner].Name;
        LogAnswering(_logger, name);
    }

    private async Task<byte[]> SendAsync(DohServer server, byte[] body, CancellationToken cancellationToken)
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = new MediaTypeHeaderValue(MediaType);
        using var request = new HttpRequestMessage(HttpMethod.Post, server.Endpoint)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Content = content,
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaType));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new DohException($"{server.Name} trả HTTP {(int)response.StatusCode}.");
        if (response.Content.Headers.ContentType?.MediaType != MediaType)
            throw new DohException($"{server.Name} trả Content-Type lạ.");
        if (response.Content.Headers.ContentLength > MaxResponseBytes)
            throw new DohException($"{server.Name} trả response quá lớn.");
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.Length > MaxResponseBytes)
            throw new DohException($"{server.Name} trả response quá lớn.");
        return bytes;
    }

    private static async ValueTask<Stream> ConnectToBootstrapAsync(
        IReadOnlyList<DohServer> servers, SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var server = servers.FirstOrDefault(s => string.Equals(s.Endpoint.Host, host, StringComparison.OrdinalIgnoreCase)
                                                 || string.Equals(s.Endpoint.DnsSafeHost, host, StringComparison.OrdinalIgnoreCase))
                     ?? throw new DohException($"Host DoH lạ: {host}.");
        SocketException? last = null;
        foreach (var address in server.BootstrapAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            var connected = false;
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                var stream = new NetworkStream(socket, ownsSocket: true);
                connected = true;
                return stream;
            }
            catch (SocketException ex)
            {
                last = ex;
            }
            finally
            {
                // Lỗi socket, timeout hay huỷ: đóng ngay, không chờ GC.
                if (!connected) socket.Dispose();
            }
        }
        var message = $"Không nối được tới {server.Name}.";
        throw last is null ? new DohException(message) : new DohException(message, last);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "DoH {Server} lỗi, chuyển sang máy chủ kế")]
    private static partial void LogServerFailed(ILogger logger, string server, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "DoH đang trả lời: {Server}")]
    private static partial void LogAnswering(ILogger logger, string server);
}
