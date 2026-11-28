using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LunaDns.Core.Dns;

/// <summary>Stub DNS nghe UDP + TCP trên cùng một endpoint (mặc định 127.0.0.1:53), bind exclusive (spec 4.2).</summary>
public sealed partial class DohStubServer : IAsyncDisposable
{
    public static readonly IPEndPoint DefaultEndPoint = new(IPAddress.Loopback, 53);
    private const int MaxTcpConnections = 64;
    internal const int MaxUdpInFlight = 256;
    private static readonly TimeSpan TcpIdleTimeout = TimeSpan.FromSeconds(10);

    private readonly DnsQueryHandler _handler;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _tcpSlots = new(MaxTcpConnections);
    private readonly SemaphoreSlim _udpSlots;
    private Socket? _udp;
    private Socket? _tcp;
    private Task _loops = Task.CompletedTask;

    private DohStubServer(DnsQueryHandler handler, ILogger logger, int maxUdpInFlight)
    {
        _handler = handler;
        _logger = logger;
        _udpSlots = new SemaphoreSlim(maxUdpInFlight);
    }

    public IPEndPoint LocalEndPoint { get; private set; } = DefaultEndPoint;

    /// <summary>Xong khi vòng nghe dừng; faulted nếu socket lỗi bất ngờ. Watchdog (KH5) chờ task này.</summary>
    public Task Completion => _loops;

    public static DohStubServer Start(IPEndPoint endPoint, DnsQueryHandler handler, ILogger<DohStubServer>? logger = null) =>
        Start(endPoint, handler, logger, MaxUdpInFlight);

    internal static DohStubServer Start(IPEndPoint endPoint, DnsQueryHandler handler, ILogger<DohStubServer>? logger, int maxUdpInFlight)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxUdpInFlight, 1);
        if (!IPAddress.IsLoopback(endPoint.Address))
            throw new ArgumentException("Stub chỉ được nghe trên địa chỉ loopback.", nameof(endPoint));
        var server = new DohStubServer(handler, (ILogger?)logger ?? NullLogger.Instance, maxUdpInFlight);
        try
        {
            server.Bind(endPoint);
        }
        catch
        {
            server._udp?.Dispose();
            server._tcp?.Dispose();
            throw;
        }
        server._loops = Task.WhenAll(Task.Run(server.UdpLoopAsync), Task.Run(server.TcpAcceptLoopAsync));
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_stop.IsCancellationRequested) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        _udp?.Dispose();
        _tcp?.Dispose();
        try
        {
            await _loops.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Vòng nghe đã chết từ trước (Completion faulted); Dispose không ném lại.
            LogLoopFaulted(_logger, ex);
        }
        // Cố ý không Dispose _stop và _tcpSlots: gói và kết nối đang xử lý dở vẫn đọc token và trả slot.
    }

    /// <summary>Cổng 0: UDP lấy cổng trống bất kỳ nhưng TCP cùng số có thể đang bị chiếm, nên thử cổng khác vài lần.</summary>
    private const int EphemeralBindAttempts = 8;

    private void Bind(IPEndPoint endPoint)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                BindOnce(endPoint);
                return;
            }
            catch (DnsStubException ex) when (endPoint.Port == 0 && ex.Error == DnsStubError.PortInUse && attempt < EphemeralBindAttempts)
            {
                _udp?.Dispose();
                _tcp?.Dispose();
                _udp = null;
                _tcp = null;
            }
        }
    }

    private void BindOnce(IPEndPoint endPoint)
    {
        _udp = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp) { ExclusiveAddressUse = true };
        BindOrThrow(_udp, endPoint);
        var bound = new IPEndPoint(endPoint.Address, ((IPEndPoint)_udp.LocalEndPoint!).Port);
        _tcp = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true, NoDelay = true };
        BindOrThrow(_tcp, bound);
        _tcp.Listen(MaxTcpConnections);
        LocalEndPoint = bound;
    }

    private static void BindOrThrow(Socket socket, IPEndPoint endPoint)
    {
        try
        {
            socket.Bind(endPoint);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            throw new DnsStubException(DnsStubError.PortInUse, $"Cổng {endPoint} đang bị ứng dụng khác dùng.", ex);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            throw new DnsStubException(DnsStubError.AccessDenied,
                $"Không mở được cổng {endPoint}: cổng bị chặn hoặc ứng dụng khác đang giữ.", ex);
        }
    }

    private async Task UdpLoopAsync()
    {
        var buffer = new byte[ushort.MaxValue];
        EndPoint anyRemote = new IPEndPoint(
            LocalEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
        while (true)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await _udp!.ReceiveFromAsync(buffer, SocketFlags.None, anyRemote, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
            {
                // Windows báo ICMP "port unreachable" của một gói trả lời trước: client đã đi, nghe tiếp.
                LogUdpReset(_logger);
                continue;
            }
            if (!_udpSlots.Wait(0))
            {
                LogUdpBusy(_logger);
                continue;
            }
            var packet = buffer.AsSpan(0, received.ReceivedBytes).ToArray();
            _ = RespondUdpAsync(packet, received.RemoteEndPoint);
        }
    }

    private async Task RespondUdpAsync(byte[] packet, EndPoint client)
    {
        try
        {
            var response = await _handler.HandleAsync(packet, DnsTransport.Udp, _stop.Token).ConfigureAwait(false);
            if (response is not null)
                await _udp!.SendToAsync(response, SocketFlags.None, client, _stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Ranh giới của một gói: gói lỗi không được làm chết stub.
            if (!_stop.IsCancellationRequested) LogPacketFailed(_logger, ex);
        }
        finally
        {
            _udpSlots.Release();
        }
    }

    private async Task TcpAcceptLoopAsync()
    {
        while (true)
        {
            Socket client;
            try
            {
                client = await _tcp!.AcceptAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            if (!_tcpSlots.Wait(0))
            {
                LogTcpBusy(_logger);
                client.Dispose();
                continue;
            }
            _ = ServeTcpAsync(client);
        }
    }

    private async Task ServeTcpAsync(Socket client)
    {
        var stream = new NetworkStream(client, ownsSocket: true);
        try
        {
            var prefix = new byte[2];
            while (true)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                idle.CancelAfter(TcpIdleTimeout);
                if (!await ReadExactAsync(stream, prefix, idle.Token).ConfigureAwait(false)) break;
                var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
                if (length < DnsWire.HeaderLength) break;
                var packet = new byte[length];
                if (!await ReadExactAsync(stream, packet, idle.Token).ConfigureAwait(false)) break;

                var response = await _handler.HandleAsync(packet, DnsTransport.Tcp, _stop.Token).ConfigureAwait(false);
                if (response is null) break;
                var framed = new byte[2 + response.Length];
                BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)response.Length);
                response.CopyTo(framed, 2);
                await stream.WriteAsync(framed, _stop.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Rảnh quá 10 s (OperationCanceledException) là đóng bình thường; lỗi khác chỉ đóng kết nối này.
            if (ex is not OperationCanceledException && !_stop.IsCancellationRequested) LogTcpFailed(_logger, ex);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            _tcpSlots.Release();
        }
    }

    /// <summary>Đọc đủ buffer. Client đóng kết nối giữa chừng thì trả false.</summary>
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        try
        {
            await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "UDP: client đã đóng cổng trước khi nhận trả lời")]
    private static partial void LogUdpReset(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Xử lý gói UDP lỗi")]
    private static partial void LogPacketFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quá nhiều gói UDP đang xử lý, bỏ gói mới")]
    private static partial void LogUdpBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Quá 64 kết nối TCP, đóng kết nối mới")]
    private static partial void LogTcpBusy(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Kết nối TCP lỗi")]
    private static partial void LogTcpFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Vòng nghe của stub đã dừng vì lỗi")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
