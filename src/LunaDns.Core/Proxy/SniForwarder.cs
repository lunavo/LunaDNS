using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Domains;
using LunaDns.Core.Tls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Proxy;

/// <summary>
/// Listener TLS trong suốt của Chế độ PUBG (spec 4.4). File hosts trỏ host PUBG về 127.0.0.1; listener đọc SNI của
/// ClientHello, nối tới máy chủ thật qua <see cref="IUpstreamConnector"/> (resolver DoH, không qua DNS hệ thống vì hosts
/// trỏ về chính nó), ghi ClientHello cắt gói rồi chuyển byte hai chiều. Không có SNI hoặc SNI ngoài danh sách thì đóng.
/// Không giải mã TLS.
/// </summary>
public sealed partial class SniForwarder : IAsyncDisposable
{
    public static readonly IPEndPoint DefaultEndPoint = new(IPAddress.Loopback, SplitProxy.TlsPort);

    private const int MaxConnections = 256;
    private static readonly TimeSpan FirstBytesTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly SniForwarderOptions _options;
    private readonly IUpstreamConnector _connector;
    private readonly ILogger _logger;
    private readonly Socket _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _slots = new(MaxConnections);
    private Task _loop = Task.CompletedTask;
    private int _active;

    private SniForwarder(Socket listener, SniForwarderOptions options, IUpstreamConnector connector, ILogger logger)
    {
        _listener = listener;
        _options = options;
        _connector = connector;
        _logger = logger;
        LocalEndPoint = (IPEndPoint)listener.LocalEndPoint!;
    }

    public IPEndPoint LocalEndPoint { get; }

    public int ActiveConnections => Volatile.Read(ref _active);

    /// <summary>Xong khi vòng nghe dừng; faulted nếu socket lỗi bất ngờ. Watchdog của FeatureSet chờ task này.</summary>
    public Task Completion => _loop;

    public static SniForwarder Start(IPEndPoint endPoint, SniForwarderOptions options, IUpstreamConnector connector, ILogger<SniForwarder>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connector);
        options.Validate();

        var listener = Bind(endPoint);
        SniForwarder forwarder;
        try
        {
            listener.Listen(MaxConnections);
            forwarder = new SniForwarder(listener, options, connector, (ILogger?)logger ?? NullLogger.Instance);
        }
        catch
        {
            listener.Dispose();
            throw;
        }
        forwarder._loop = Task.Run(forwarder.AcceptLoopAsync);
        return forwarder;
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

    private static Socket Bind(IPEndPoint endPoint)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        try
        {
            socket.Bind(endPoint);
            return socket;
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            var error = ex.SocketErrorCode switch
            {
                SocketError.AddressAlreadyInUse => ProxyStartError.PortInUse,
                SocketError.AccessDenied => ProxyStartError.AccessDenied,
                _ => ProxyStartError.Other,
            };
            throw new SplitProxyException(error, $"Không mở được cổng {endPoint} cho Chế độ PUBG.", ex);
        }
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
            int count;
            int recordLength;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                timeout.CancelAfter(FirstBytesTimeout);
                (count, recordLength) = await TlsTunnel.ReadFirstRecordAsync(stream, buffer, 0, timeout.Token).ConfigureAwait(false);
            }
            if (recordLength < 0 || count < recordLength
                || !ClientHelloParser.TryFindSni(buffer.AsSpan(0, recordLength), out var sni)
                || !DomainMatcher.Matches(sni.Host, _options.Domains))
            {
                // Không ghi SNI: có thể là host ngoài GameDomains (log nằm trong báo cáo chẩn đoán).
                LogRefused(_logger);
                return;
            }
            await ForwardAsync(stream, sni.Host, buffer, count, recordLength).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ranh giới của một kết nối: lỗi ở đây chỉ đóng kết nối này.
            if (ex is not OperationCanceledException && !_stop.IsCancellationRequested) LogConnectionFailed(_logger, ex);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            Interlocked.Decrement(ref _active);
            _slots.Release();
        }
    }

    private async Task ForwardAsync(NetworkStream client, string host, byte[] buffer, int count, int recordLength)
    {
        Stream upstream;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            connectTimeout.CancelAfter(ConnectTimeout);
            upstream = await _connector.ConnectAsync(host, SplitProxy.TlsPort, connectTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (!_stop.IsCancellationRequested)
        {
            LogUpstreamFailed(_logger, host, ex);
            return;
        }

        try
        {
            await TlsTunnel.WriteFirstRecordAsync(upstream, buffer, count, recordLength, _options.SplitMode, _options.SplitDelay, _stop.Token).ConfigureAwait(false);
            var ended = await TlsTunnel.RelayAsync(client, upstream, _stop.Token).ConfigureAwait(false);
            if (ended is not null) LogRelayEnded(_logger, ended);
        }
        finally
        {
            await upstream.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quá 256 kết nối tới listener PUBG, đóng kết nối mới")]
    private static partial void LogBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Đóng kết nối tới listener PUBG: không phải ClientHello, không có SNI hoặc SNI ngoài danh sách")]
    private static partial void LogRefused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listener PUBG không nối được tới {Host}")]
    private static partial void LogUpstreamFailed(ILogger logger, string host, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kết nối tới listener PUBG lỗi")]
    private static partial void LogConnectionFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Tunnel PUBG đã đóng")]
    private static partial void LogRelayEnded(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vòng nghe của listener PUBG đã dừng vì lỗi")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
