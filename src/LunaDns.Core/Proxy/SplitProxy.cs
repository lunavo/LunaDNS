using System.Net;
using System.Net.Sockets;
using System.Text;
using LunaDns.Core.Domains;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Proxy;

/// <summary>
/// Proxy HTTP CONNECT trên 127.0.0.1 (spec 4.3): phục vụ /proxy.pac, chỉ nhận CONNECT tới domain game ở cổng 443,
/// cắt ClientHello thành nhiều segment rồi chuyển byte hai chiều. Không giải mã TLS.
/// Giới hạn đã đo ở spike: chỉ phần CEF của Steam (Store, Community) đọc PAC; kết nối CM không đi qua proxy này.
/// </summary>
public sealed partial class SplitProxy : IAsyncDisposable
{
    public const int TlsPort = 443;
    private const int MaxConnections = 256;
    private static readonly TimeSpan FirstBytesTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private static readonly byte[] Established = "HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray();
    private static readonly byte[] BadRequest = Status("400 Bad Request");
    private static readonly byte[] Forbidden = Status("403 Forbidden");
    private static readonly byte[] NotFound = Status("404 Not Found");
    private static readonly byte[] MethodNotAllowed = Status("405 Method Not Allowed");
    private static readonly byte[] HeadTooLarge = Status("431 Request Header Fields Too Large");
    private static readonly byte[] BadGateway = Status("502 Bad Gateway");

    private readonly SplitProxyOptions _options;
    private readonly IUpstreamConnector _connector;
    private readonly ILogger _logger;
    private readonly Socket _listener;
    private readonly byte[] _pacResponse;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections);
    private Task _loop = Task.CompletedTask;
    private int _active;

    private SplitProxy(Socket listener, SplitProxyOptions options, IUpstreamConnector connector, ILogger logger)
    {
        _listener = listener;
        _options = options;
        _connector = connector;
        _logger = logger;
        LocalEndPoint = (IPEndPoint)listener.LocalEndPoint!;
        PacUrl = new Uri($"http://127.0.0.1:{LocalEndPoint.Port}/proxy.pac");

        var pac = Encoding.ASCII.GetBytes(PacBuilder.Build(LocalEndPoint.Port, options.Domains));
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Type: application/x-ns-proxy-autoconfig\r\n" +
            $"Content-Length: {pac.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        _pacResponse = [.. head, .. pac];
    }

    public IPEndPoint LocalEndPoint { get; }

    public int Port => LocalEndPoint.Port;

    /// <summary>Địa chỉ PAC để KH4 ghi vào AutoConfigURL.</summary>
    public Uri PacUrl { get; }

    public int ActiveConnections => Volatile.Read(ref _active);

    /// <summary>Xong khi vòng nghe dừng; faulted nếu socket lỗi bất ngờ. Watchdog (KH5) chờ task này.</summary>
    public Task Completion => _loop;

    public static SplitProxy Start(SplitProxyOptions options, IUpstreamConnector connector, ILogger<SplitProxy>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connector);
        options.Validate();

        var listener = BindFirstFreePort(options.FirstPort, options.LastPort);
        SplitProxy proxy;
        try
        {
            listener.Listen(MaxConnections);
            proxy = new SplitProxy(listener, options, connector, (ILogger?)logger ?? NullLogger.Instance);
        }
        catch
        {
            listener.Dispose();
            throw;
        }
        proxy._loop = Task.Run(proxy.AcceptLoopAsync);
        return proxy;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();
        try
        {
            await _loop.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Vòng nghe đã chết từ trước (Completion faulted); Dispose không ném lại.
            LogLoopFaulted(_logger, ex);
        }
        // Cố ý không Dispose _stop và _slots: kết nối đang xử lý dở vẫn đọc token và trả slot.
    }

    private static byte[] Status(string statusLine) =>
        Encoding.ASCII.GetBytes($"HTTP/1.1 {statusLine}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private static Socket BindFirstFreePort(int firstPort, int lastPort)
    {
        SocketException? last = null;
        var error = ProxyStartError.AccessDenied;
        for (var port = firstPort; port <= lastPort; port++)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                return socket;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                // Cổng đang có app khác giữ (Windows có khi báo AccessDenied thay vì AddressAlreadyInUse): thử cổng kế.
                socket.Dispose();
                last = ex;
                if (ex.SocketErrorCode == SocketError.AddressAlreadyInUse) error = ProxyStartError.PortInUse;
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                throw new SplitProxyException(ProxyStartError.Other, $"Không mở được cổng proxy 127.0.0.1:{port}.", ex);
            }
        }
        throw new SplitProxyException(error, $"Không còn cổng trống trong khoảng {firstPort}–{lastPort} cho proxy.", last!);
    }

    private async Task AcceptLoopAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _listener.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            if (!_slots.Wait(0))
            {
                LogBusy(_logger);
                client.Dispose();
                continue;
            }
            client.NoDelay = true;
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(Socket client)
    {
        Interlocked.Increment(ref _active);
        var stream = new NetworkStream(client, ownsSocket: true);
        try
        {
            var buffer = new byte[TlsTunnel.BufferSize];
            var (request, count) = await ReadRequestAsync(stream, buffer).ConfigureAwait(false);
            if (request is null) return;
            if (request.ConnectHost is null)
                await stream.WriteAsync(LocalResponse(request), _stop.Token).ConfigureAwait(false);
            else
                await TunnelAsync(stream, request.ConnectHost, request.ConnectPort, buffer, count).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ranh giới của một kết nối: lỗi ở đây chỉ đóng kết nối này, không làm chết proxy.
            // Client im lặng quá 10 giây (OperationCanceledException) là đóng bình thường.
            if (ex is not OperationCanceledException && !_stop.IsCancellationRequested) LogConnectionFailed(_logger, ex);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref _active);
            _slots.Release();
        }
    }

    /// <summary>
    /// Đọc và phân tích request head. Trả request null nếu client đóng sớm hoặc đã trả lỗi 400/431.
    /// Count là số byte client gửi liền sau head, đã được dời về đầu buffer.
    /// </summary>
    private async Task<(ProxyRequest? Request, int Count)> ReadRequestAsync(NetworkStream stream, byte[] buffer)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(FirstBytesTimeout);
        var count = 0;
        int headEnd;
        while ((headEnd = ProxyRequestParser.FindHeadEnd(buffer.AsSpan(0, count))) < 0)
        {
            if (count >= ProxyRequestParser.MaxHeadLength)
            {
                await stream.WriteAsync(HeadTooLarge, _stop.Token).ConfigureAwait(false);
                return (null, 0);
            }
            var read = await stream.ReadAsync(buffer.AsMemory(count, ProxyRequestParser.MaxHeadLength - count), timeout.Token).ConfigureAwait(false);
            if (read == 0) return (null, 0);
            count += read;
        }

        if (!ProxyRequestParser.TryParse(buffer.AsSpan(0, headEnd), out var request))
        {
            await stream.WriteAsync(BadRequest, _stop.Token).ConfigureAwait(false);
            return (null, 0);
        }
        buffer.AsSpan(headEnd, count - headEnd).CopyTo(buffer);
        return (request, count - headEnd);
    }

    private byte[] LocalResponse(ProxyRequest request)
    {
        // Target dạng tuyệt đối (GET http://host/...) là xin làm proxy HTTP thường: từ chối như host ngoài danh sách.
        if (request.Target.Contains("://", StringComparison.Ordinal)) return Forbidden;
        if (request.Method != "GET") return MethodNotAllowed;
        return request.Target == "/proxy.pac" ? _pacResponse : NotFound;
    }

    private async Task TunnelAsync(NetworkStream client, string host, int port, byte[] buffer, int count)
    {
        if (port != TlsPort || !DomainMatcher.Matches(host, _options.Domains))
        {
            // Không ghi tên host: log có thể nằm trong báo cáo chẩn đoán gửi qua chat.
            LogRefused(_logger);
            await client.WriteAsync(Forbidden, _stop.Token).ConfigureAwait(false);
            return;
        }

        Stream upstream;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            connectTimeout.CancelAfter(ConnectTimeout);
            upstream = await _connector.ConnectAsync(host, port, connectTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            LogUpstreamFailed(_logger, host, ex);
            await client.WriteAsync(BadGateway, _stop.Token).ConfigureAwait(false);
            return;
        }

        try
        {
            await client.WriteAsync(Established, _stop.Token).ConfigureAwait(false);
            await ForwardFirstRecordAsync(client, upstream, buffer, count).ConfigureAwait(false);
            await RelayAsync(client, upstream).ConfigureAwait(false);
        }
        finally
        {
            await upstream.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Đọc trọn TLS record đầu tiên của client rồi ghi cắt gói sang máy chủ. Dữ liệu không phải record handshake,
    /// hoặc record khai dài quá cỡ, thì chuyển nguyên.
    /// </summary>
    private async Task ForwardFirstRecordAsync(Stream client, Stream upstream, byte[] buffer, int count)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(FirstBytesTimeout);
        var (filled, recordLength) = await TlsTunnel.ReadFirstRecordAsync(client, buffer, count, timeout.Token).ConfigureAwait(false);
        await TlsTunnel.WriteFirstRecordAsync(upstream, buffer, filled, recordLength, _options.Split, _options.SplitDelay, _stop.Token).ConfigureAwait(false);
    }

    /// <summary>Chuyển byte hai chiều tới khi một bên đóng, rồi dừng chiều còn lại.</summary>
    private async Task RelayAsync(Stream client, Stream upstream)
    {
        var ended = await TlsTunnel.RelayAsync(client, upstream, _stop.Token).ConfigureAwait(false);
        if (ended is not null) LogRelayEnded(_logger, ended);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quá 256 kết nối tới proxy, đóng kết nối mới")]
    private static partial void LogBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Từ chối CONNECT: host ngoài danh sách domain game hoặc cổng khác 443")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Không nối được tới {Host}, trả 502")]
    private static partial void LogUpstreamFailed(ILogger logger, string host, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kết nối tới proxy lỗi")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tunnel đã đóng")]
    private static partial void LogRelayEnded(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vòng nghe của proxy đã dừng vì lỗi")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
