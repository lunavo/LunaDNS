using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LunaDns.Platform.MacOS.Daemon;

/// <summary>Gửi một yêu cầu tới daemon. Daemon chưa cài hoặc chưa chạy thì ném DaemonUnavailableException.</summary>
public sealed class DaemonClient(string socketPath = DaemonProtocol.SocketPath)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<DaemonResponse> SendAsync(DaemonRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new DaemonUnavailableException("Dịch vụ nền LunaDNS chưa chạy. Cài bằng: sudo lunadns install", ex);
        }

        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, DaemonProtocol.RequestInfo) + "\n");
        await socket.SendAsync(payload, SocketFlags.None, timeout.Token).ConfigureAwait(false);

        using var stream = new NetworkStream(socket, ownsSocket: false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false)
            ?? throw new DaemonUnavailableException("Dịch vụ nền LunaDNS đóng kết nối mà không trả lời.");
        return JsonSerializer.Deserialize(line, DaemonProtocol.ResponseInfo)
            ?? throw new DaemonUnavailableException("Dịch vụ nền LunaDNS trả lời rỗng.");
    }
}

public sealed class DaemonUnavailableException : Exception
{
    public DaemonUnavailableException()
    {
    }

    public DaemonUnavailableException(string message) : base(message)
    {
    }

    public DaemonUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
