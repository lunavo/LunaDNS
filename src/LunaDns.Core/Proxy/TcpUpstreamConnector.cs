using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;
using LunaDns.Core.Net;

namespace LunaDns.Core.Proxy;

/// <summary>
/// Nối thẳng bằng socket (không bao giờ qua proxy hệ thống), NoDelay bật sẵn để hai mảnh ClientHello thành hai segment.
/// Nên đưa vào <see cref="DohHostResolver"/>: proxy khi đó không phụ thuộc DNS hệ thống hay NRPT.
/// Tối đa 3 IP, nối so le (B9): IP đang nối quá <see cref="DefaultAttemptDelay"/> thì mở IP kế song song, IP lỗi thì sang
/// IP kế ngay. Cả cuộc đua nằm trong ngân sách (token) của bên gọi.
/// </summary>
public sealed class TcpUpstreamConnector : IUpstreamConnector
{
    public static readonly TimeSpan DefaultAttemptDelay = TimeSpan.FromSeconds(1);
    private const int MaxAddresses = 3;
    private readonly IHostResolver _resolver;
    private readonly TimeProvider _time;
    private readonly TimeSpan _attemptDelay;
    private readonly Func<IPEndPoint, CancellationToken, Task<Stream>> _connect;

    public TcpUpstreamConnector(IHostResolver resolver)
        : this(resolver, TimeProvider.System, DefaultAttemptDelay, ConnectSocketAsync)
    {
    }

    /// <summary>Cho test: đồng hồ giả và hàm nối giả.</summary>
    internal TcpUpstreamConnector(IHostResolver resolver, TimeProvider time, TimeSpan attemptDelay,
        Func<IPEndPoint, CancellationToken, Task<Stream>> connect)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
        _time = time;
        _attemptDelay = attemptDelay;
        _connect = connect;
    }

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var addresses = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0)
            throw new IOException($"Không phân giải được {host}.");

        var candidates = addresses.Take(MaxAddresses).ToArray();
        var result = await StaggeredRace.RunAsync(candidates.Length, _attemptDelay, _time,
            (i, ct) => _connect(new IPEndPoint(candidates[i], port), ct),
            ex => ex is SocketException,
            cancellationToken,
            discard: stream => stream.Dispose()).ConfigureAwait(false);
        if (result.Succeeded) return result.Value!;
        throw new IOException($"Không kết nối được tới {host}:{port}.", result.Failures[^1]);
    }

    private static async Task<Stream> ConnectSocketAsync(IPEndPoint endPoint, CancellationToken cancellationToken)
    {
        var socket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
