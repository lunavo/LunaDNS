using System.Net;
using System.Net.Sockets;
using LunaDns.Core.Dns;

namespace LunaDns.Core.Proxy;

/// <summary>
/// Nối thẳng bằng socket (không bao giờ qua proxy hệ thống), NoDelay bật sẵn để hai mảnh ClientHello thành hai segment.
/// Nên đưa vào <see cref="DohHostResolver"/>: proxy khi đó không phụ thuộc DNS hệ thống hay NRPT.
/// </summary>
public sealed class TcpUpstreamConnector : IUpstreamConnector
{
    private const int MaxAddresses = 3;
    private readonly IHostResolver _resolver;

    public TcpUpstreamConnector(IHostResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var addresses = await _resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Count == 0)
            throw new IOException($"Không phân giải được {host}.");

        SocketException? last = null;
        foreach (var address in addresses.Take(MaxAddresses))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                socket.Dispose();
                last = ex;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new IOException($"Không kết nối được tới {host}:{port}.", last);
    }
}
